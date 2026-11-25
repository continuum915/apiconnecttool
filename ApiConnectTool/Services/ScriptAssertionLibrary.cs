namespace ApiConnectTool.Services;

/// <summary>
/// The Chai-style assertion chains behind <c>pm.expect</c> and <c>pm.response.to</c>, as
/// JavaScript spliced into <see cref="ScriptSandbox"/>'s bootstrap. It relies on the
/// bootstrap's captured helpers (<c>seed</c>, <c>describe</c>, <c>stringify</c>,
/// <c>hasOwn</c>, <c>keys</c>, <c>isArray</c>, <c>slice</c>, <c>ProxyType</c>) and defines
/// <c>chain(subject, matchers, extra, negate)</c> and <c>expectMatchers(value)</c>.
/// </summary>
internal static class ScriptAssertionLibrary
{
    public const string Source = """
            // Chai-style chains: language words pass through, `not` negates, and a matcher either
            // runs on access (`ok`, `exist`) or is called. Any other member fails as unsupported
            // instead of surfacing as a TypeError.
            const chainWords = new Set(['to', 'be', 'been', 'is', 'that', 'which', 'and', 'has', 'have',
                'with', 'at', 'of', 'same', 'but', 'does', 'still', 'also']);
            const quietKeys = new Set(['then', 'toJSON', 'constructor', 'toString', 'valueOf', 'inspect', 'asymmetricMatch', 'nodeType']);
            function deepEqual(a, b) {
                if (a === b) return true;
                if (typeof a === 'number' && typeof b === 'number') return a !== a && b !== b;
                if (!a || !b || typeof a !== 'object' || typeof b !== 'object') return false;
                if (isArray(a) !== isArray(b)) return false;
                const keysA = keys(a), keysB = keys(b);
                if (keysA.length !== keysB.length) return false;
                for (const key of keysA) if (!hasOwn(b, key) || !deepEqual(a[key], b[key])) return false;
                return true;
            }
            function includes(subject, expected) {
                if (typeof subject === 'string') return subject.indexOf(stringify(expected)) >= 0;
                if (isArray(subject)) { for (const item of subject) if (item === expected) return true; return false; }
                if (subject && typeof subject === 'object' && expected && typeof expected === 'object') {
                    for (const key of keys(expected)) if (!hasOwn(subject, key) || subject[key] !== expected[key]) return false;
                    return true;
                }
                return false;
            }
            function numeric(subject, operand) {
                if (typeof subject !== 'number' || typeof operand !== 'number')
                    throw new Error(seed.notNumber.replace('{0}', describe(subject)).replace('{1}', describe(operand)));
            }
            function expectMatchers(value) {
                const equalTo = {verb: 'equal', test: expected => value === expected};
                const include = {verb: 'include', test: expected => includes(value, expected)};
                const above = {verb: 'be above', test: expected => { numeric(value, expected); return value > expected; }};
                const below = {verb: 'be below', test: expected => { numeric(value, expected); return value < expected; }};
                return {
                    equal: equalTo, equals: equalTo, eq: equalTo,
                    eql: {verb: 'deeply equal', test: expected => deepEqual(value, expected)},
                    include, includes: include, contain: include, contains: include,
                    property: {verb: 'have property',
                        operand: args => args.length > 1 ? stringify(args[0]) + ' = ' + describe(args[1]) : stringify(args[0]),
                        test: function (name) {
                            if (value === null || value === undefined || !(stringify(name) in Object(value))) return false;
                            return arguments.length < 2 || value[stringify(name)] === arguments[1];
                        }},
                    above, gt: above, greaterThan: above,
                    below, lt: below, lessThan: below,
                    exist: {onAccess: true, verb: 'exist', test: () => value !== null && value !== undefined},
                    ok: {onAccess: true, verb: 'be truthy', test: () => !!value}
                };
            }
            function chain(subject, matchers, extra, negate) {
                let proxy;
                function run(matcher, args) {
                    const passed = !!matcher.test.apply(null, args);
                    if (passed === negate) {
                        const operand = matcher.onAccess ? '' : ' ' + (matcher.operand ? matcher.operand(args) : describe(args[0]));
                        throw new Error((negate ? seed.expectFailedNot : seed.expectFailed)
                            .replace('{0}', subject).replace('{1}', matcher.verb).replace('{2}', operand));
                    }
                    return proxy;
                }
                proxy = new ProxyType(Object.create(null), {
                    get: (_, key) => {
                        if (typeof key !== 'string') return undefined;
                        if (key === 'not') return chain(subject, matchers, null, !negate);
                        if (chainWords.has(key)) return proxy;
                        if (hasOwn(matchers, key)) {
                            const matcher = matchers[key];
                            if (matcher.onAccess) return run(matcher, []);
                            return function () { return run(matcher, slice(arguments)); };
                        }
                        if (extra && hasOwn(extra, key)) return extra[key];
                        if (quietKeys.has(key)) return undefined;
                        throw new Error(seed.notSupported.replace('{0}', key));
                    },
                    set: () => false, defineProperty: () => false, deleteProperty: () => false
                });
                return proxy;
            }
        """;
}
