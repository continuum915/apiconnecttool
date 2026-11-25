using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Jint;
using Jint.Native;
using Jint.Runtime;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Services;

/// <summary>Each invocation owns a fresh engine. Only JSON data crosses the host boundary.</summary>
public sealed class ScriptSandbox : IScriptSandbox
{
    public ScriptRunResult Run(string script, RequestItem request, RequestExecutionResult? response,
        IReadOnlyDictionary<string, string> variables, IReadOnlySet<string> secretNames,
        IReadOnlyList<string> secretValues, int timeoutMilliseconds = 2000,
        CancellationToken cancellationToken = default, ScriptVariableScopes? scopes = null)
    {
        var tests = new List<AssertionResult>();
        var finalVariables = new Dictionary<string, string>(variables);
        var secrets = new List<string>(secretValues);
        secrets.AddRange(RequestSecrets.Collect(request, variables, secretNames));
        if (scopes is not null)
        {
            secrets.AddRange(scopes.SecretValues());
            secretNames = secretNames.Concat(scopes.SecretNames()).ToHashSet();
        }
        var console = new List<ScriptConsoleLine>();
        var outcome = ScriptOutcome.Completed;
        string? error = null;
        JsValue? state = null;
        try
        {
            var timeout = TimeSpan.FromMilliseconds(Math.Clamp(timeoutMilliseconds, 1, 10000));
            using var engine = new Engine(options =>
            {
                options.LimitMemory(16 * 1024 * 1024).LimitRecursion(200).MaxStatements(50000)
                    .TimeoutInterval(timeout).CancellationToken(cancellationToken);
                options.Interop.Enabled = false;
                options.Interop.AllowSystemReflection = false;
                options.Host.StringCompilationAllowed = false;
                options.Constraints.RegexTimeout = timeout;
            });
            var seed = JsonSerializer.Serialize(new
            {
                variables, secretNames = secretNames.ToArray(), scopes = scopes?.Seed(),
                request = new { method = request.Method.Method, url = request.Url,
                    headers = Headers(request.Headers), body = request.Body },
                // A truncated preview is never seeded, so no script path can mistake it for the full body.
                response = response is null ? null : new { status = (int?)response.StatusCode,
                    headers = Headers(response.Headers), body = response.BodyWasTruncated ? null : Decode(response),
                    bodyTruncated = response.BodyWasTruncated, responseTime = response.Elapsed.TotalMilliseconds },
                mismatch = DefaultStrings.Get("Str.Scripts.Mismatch"),
                bodyTruncated = DefaultStrings.Get("Str.Scripts.BodyTruncated"),
                scopeUnavailable = DefaultStrings.Get("Str.Scripts.ScopeUnavailable"),
                expectFailed = DefaultStrings.Get("Str.Scripts.ExpectFailed"),
                expectFailedNot = DefaultStrings.Get("Str.Scripts.ExpectFailedNot"),
                notSupported = DefaultStrings.Get("Str.Scripts.AssertionNotSupported"),
                notNumber = DefaultStrings.Get("Str.Scripts.ExpectNotNumber")
            });
            state = engine.Evaluate(Bootstrap.Replace("ASSERTION_LIBRARY", ScriptAssertionLibrary.Source, StringComparison.Ordinal)
                .Replace("SEED_JSON", JsonSerializer.Serialize(seed), StringComparison.Ordinal)
                .Replace("CONSOLE_MAX_LINES", ScriptConsoleLine.MaxLinesPerRun.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("CONSOLE_MAX_CHARACTERS", ScriptConsoleLine.MaxCharactersPerRun.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
            engine.SetValue("pm", state.AsObject().Get("pm"));
            engine.SetValue("console", state.AsObject().Get("console"));
            engine.Execute(script);
        }
        catch (Exception exception)
        {
            outcome = exception switch
            {
                TimeoutException => ScriptOutcome.TimedOut,
                MemoryLimitExceededException or StatementsCountOverflowException or RecursionDepthOverflowException => ScriptOutcome.LimitExceeded,
                _ => ScriptOutcome.ScriptError
            };
            error = outcome switch
            {
                ScriptOutcome.TimedOut => DefaultStrings.Get("Str.Scripts.TimedOut"),
                ScriptOutcome.LimitExceeded => DefaultStrings.Get("Str.Scripts.LimitExceeded"),
                _ => exception.Message
            };
        }
        // Read inert values directly; never execute script getters or convert projected CLR objects.
        if (state is not null)
        {
            var data = state.AsObject();
            finalVariables.Clear();
            foreach (var pair in data.Get("variables").AsObject().GetOwnProperties())
                finalVariables[pair.Key.AsString()] = pair.Value.Value.AsString();
            var writes = data.Get("writes").AsArray();
            for (uint i = 0; i < writes.Length; i++)
            {
                var write = writes.Get(i).AsObject();
                scopes!.Apply(write.Get("scope").AsString(), write.Get("key").AsString(),
                    write.Get("unset").AsBoolean() ? null : write.Get("value").AsString());
            }
            secrets.AddRange(RequestSecrets.Collect(request, finalVariables, secretNames));
            var secretArray = data.Get("secrets").AsArray();
            for (uint i = 0; i < secretArray.Length; i++) secrets.Add(secretArray.Get(i).AsString());
            var redactor = new SecretRedactor();
            string Redact(string text) => redactor.Redact(text, secrets);
            var rows = data.Get("tests").AsArray();
            for (uint i = 0; i < rows.Length; i++)
            {
                var row = rows.Get(i).AsObject();
                var name = Redact(row.Get("name").AsString());
                if (row.Get("incomplete").AsBoolean())
                {
                    tests.Add(AssertionResult.IncompleteFor(Guid.NewGuid(), name, DefaultStrings.Get("Str.Scripts.BodyTruncated")));
                    continue;
                }
                var passed = row.Get("passed").AsBoolean();
                tests.Add(new(Guid.NewGuid(), name, passed, "", passed ? null : Redact(row.Get("error").AsString())));
            }
            var lines = data.Get("consoleLines").AsArray();
            for (uint i = 0; i < lines.Length; i++)
            {
                var line = lines.Get(i).AsObject();
                var level = Enum.TryParse<ScriptConsoleLevel>(line.Get("level").AsString(), ignoreCase: true, out var parsed) ? parsed : ScriptConsoleLevel.Log;
                console.Add(new(level, Redact(line.Get("text").AsString())));
            }
            if (data.Get("consoleState").AsObject().Get("truncated").AsBoolean())
                console.Add(new(ScriptConsoleLevel.Warn, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    DefaultStrings.Get("Str.Scripts.ConsoleTruncated"), ScriptConsoleLine.MaxLinesPerRun, ScriptConsoleLine.MaxCharactersPerRun / 1024)));
            // Counted by the bootstrap's accessors, so catching the access error cannot hide it.
            // Reads inside pm.test already marked that test; report the rest once.
            var flags = data.Get("flags").AsObject();
            if (flags.Get("truncatedBodyReads").AsNumber() > flags.Get("readsInTests").AsNumber())
                tests.Add(AssertionResult.IncompleteFor(Guid.NewGuid(), DefaultStrings.Get("Str.Scripts.IncompleteBodyAccess"),
                    DefaultStrings.Get("Str.Scripts.BodyTruncated")));
        }
        return new(outcome, error is null ? null : new SecretRedactor().Redact(error, secrets), tests, finalVariables, secrets) { Console = console };
    }

    private static Dictionary<string, string> Headers(IEnumerable<KeyValueItem> headers) => headers
        .Where(header => header.IsEnabled).GroupBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => string.Join(", ", group.Select(header => header.Value)));

    private static string Decode(RequestExecutionResult response)
    {
        try { return Encoding.GetEncoding(response.CharacterSet ?? "utf-8").GetString(response.Body); }
        catch (ArgumentException) { return Encoding.UTF8.GetString(response.Body); }
    }

    private const string Bootstrap = """
        (() => {
            const seed = JSON.parse(SEED_JSON);
            const variables = Object.assign(Object.create(null), seed.variables);
            const secretNames = new Set(seed.secretNames);
            const tests = Object.setPrototypeOf([], null), secrets = Object.setPrototypeOf([], null);
            const writes = Object.setPrototypeOf([], null);
            const stringify = String, parse = JSON.parse, equal = JSON.stringify;
            const push = Function.prototype.call.bind(Array.prototype.push);
            const freeze = Object.freeze, defineProperty = Object.defineProperty;
            const ProxyType = Proxy, keys = Object.keys, isArray = Array.isArray, slice = Function.prototype.call.bind(Array.prototype.slice);
            const flags = Object.create(null);
            flags.truncatedBodyReads = 0; flags.readsInTests = 0;
            const consoleLines = Object.setPrototypeOf([], null);
            const consoleState = Object.create(null);
            consoleState.characters = 0; consoleState.truncated = false;
            function describe(value) {
                if (typeof value === 'string') return value;
                if (value === undefined || typeof value === 'function' || typeof value === 'symbol') return stringify(value);
                try { const json = equal(value); return json === undefined ? stringify(value) : json; }
                catch (ignored) { return stringify(value); }
            }
            function writer(level) {
                return function () {
                    if (consoleState.truncated) return;
                    let text = '';
                    for (let i = 0; i < arguments.length; i++) text += (i > 0 ? ' ' : '') + describe(arguments[i]);
                    if (consoleLines.length >= CONSOLE_MAX_LINES || consoleState.characters + text.length > CONSOLE_MAX_CHARACTERS) {
                        consoleState.truncated = true;
                        return;
                    }
                    consoleState.characters += text.length;
                    push(consoleLines, {level, text});
                };
            }
            const console = freeze({log: writer('log'), info: writer('info'), warn: writer('warn'),
                error: writer('error'), debug: writer('debug')});
            const isSecret = Function.prototype.call.bind(Set.prototype.has);
            const hasOwn = Function.prototype.call.bind(Object.prototype.hasOwnProperty);
            const scopes = Object.create(null);
            for (const scope of ['globals', 'collectionVariables', 'environment']) {
                scopes[scope] = seed.scopes && seed.scopes[scope] !== null
                    ? Object.assign(Object.create(null), seed.scopes[scope]) : null;
            }
            function resolved(name) {
                if (hasOwn(variables, name)) return variables[name];
                for (const scope of ['environment', 'collectionVariables', 'globals'])
                    if (scopes[scope] && hasOwn(scopes[scope], name)) return scopes[scope][name];
            }
            function scopeApi(scope) {
                return freeze({
                    get: name => scopes[scope] ? scopes[scope][stringify(name)] : undefined,
                    has: name => !!scopes[scope] && hasOwn(scopes[scope], stringify(name)),
                    set: (name, value) => {
                        if (!scopes[scope]) throw new Error(seed.scopeUnavailable.replace('{0}', scope));
                        name = stringify(name); value = stringify(value);
                        if (isSecret(secretNames, name)) { push(secrets, scopes[scope][name] || ''); push(secrets, value); }
                        scopes[scope][name] = value;
                        push(writes, {scope, key: name, value, unset: false});
                    },
                    unset: name => {
                        if (!scopes[scope]) throw new Error(seed.scopeUnavailable.replace('{0}', scope));
                        name = stringify(name);
                        if (isSecret(secretNames, name)) push(secrets, scopes[scope][name] || '');
                        delete scopes[scope][name]; push(writes, {scope, key: name, value: '', unset: true});
                    }
                });
            }
            function deepFreeze(value) {
                if (value && typeof value === 'object') {
                    Object.keys(value).forEach(key => deepFreeze(value[key])); freeze(value);
                }
                return value;
            }
            ASSERTION_LIBRARY
            const pm = {
                variables: freeze({
                    get: name => resolved(stringify(name)),
                    set: (name, value) => {
                        name = stringify(name); value = stringify(value);
                        if (isSecret(secretNames, name)) { push(secrets, variables[name] || ''); push(secrets, value); }
                        variables[name] = value;
                    }
                }),
                environment: scopeApi('environment'), collectionVariables: scopeApi('collectionVariables'), globals: scopeApi('globals'),
                request: deepFreeze(seed.request),
                test: (name, fn) => {
                    name = stringify(name);
                    const before = flags.truncatedBodyReads;
                    let passed = true, error = '';
                    try { fn(); } catch (thrown) { passed = false; error = stringify(thrown); }
                    const reads = flags.truncatedBodyReads - before;
                    flags.readsInTests += reads;
                    push(tests, {name, passed, error, incomplete: reads > 0});
                },
                expect: value => {
                    function check(passed) { if (!passed) throw new Error(seed.mismatch); }
                    const legacy = {
                        toEqual: expected => check(equal(value) === equal(expected)),
                        toContain: expected => check(value != null && typeof value.includes === 'function' && value.includes(expected)),
                        toBeGreaterThan: expected => check(value > expected),
                        toBeLessThan: expected => check(value < expected),
                        toExist: () => check(value !== null && value !== undefined)
                    };
                    return chain(describe(value), expectMatchers(value), legacy, false);
                }
            };
            if (seed.response) {
                const body = seed.response.body, truncated = seed.response.bodyTruncated === true;
                function fullBody() {
                    if (truncated) { flags.truncatedBodyReads++; throw new Error(seed.bodyTruncated); }
                    return body;
                }
                const status = seed.response.status, headers = deepFreeze(seed.response.headers);
                function header(name) {
                    name = stringify(name).toLowerCase();
                    for (const key of keys(headers)) if (key.toLowerCase() === name) return headers[key];
                }
                const responseMatchers = {
                    status: {verb: 'have status', test: expected => status === expected},
                    header: {verb: 'have header',
                        operand: args => args.length > 1 ? stringify(args[0]) + ': ' + stringify(args[1]) : stringify(args[0]),
                        test: function (name) {
                            const actual = header(name);
                            return actual !== undefined && (arguments.length < 2 || actual === stringify(arguments[1]));
                        }},
                    // Reads go through fullBody, so a truncated body marks the test incomplete.
                    body: {verb: 'have body', operand: args => args.length ? describe(args[0]) : '',
                        test: function (expected) {
                            const text = fullBody();
                            return arguments.length === 0 ? text !== null && text !== '' : text === stringify(expected);
                        }},
                    ok: {onAccess: true, verb: 'have status 200', test: () => status === 200},
                    success: {onAccess: true, verb: 'have a 2xx status', test: () => status >= 200 && status < 300}
                };
                const response = {status, code: status, headers,
                    responseTime: seed.response.responseTime, bodyTruncated: truncated,
                    text: () => fullBody(), json: () => parse(fullBody())};
                defineProperty(response, 'to', {get: () => chain('response with status ' + stringify(status), responseMatchers, null, false)});
                // Non-enumerable when truncated so logging or spreading metadata never touches the body.
                defineProperty(response, 'body', {get: fullBody, enumerable: !truncated});
                pm.response = freeze(response);
            }
            return { pm: freeze(pm), console, variables, tests, secrets, writes, flags, consoleLines, consoleState };
        })()
        """;
}
