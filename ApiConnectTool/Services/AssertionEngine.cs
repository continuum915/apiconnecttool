using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Services;

public sealed class AssertionEngine
{
    private readonly ISecretRedactor _redactor = new SecretRedactor();

    public IReadOnlyList<AssertionResult> Evaluate(IEnumerable<Assertion> assertions, RequestExecutionResult response)
    {
        var results = new List<AssertionResult>();
        if (!response.IsSuccess || response.StatusCode is null) return results;

        foreach (var assertion in assertions)
        {
            if (!assertion.IsEnabled) continue;
            var operatorLabel = DefaultStrings.Get("Str.Assertions.Operator." + assertion.Operator);
            var subject = assertion.Kind switch
            {
                AssertionKind.Schema => DefaultStrings.Get("Str.Assertions.SchemaSubject"),
                AssertionKind.Body => DefaultStrings.Get("Str.Assertions.BodySubject"),
                AssertionKind.JsonPath => Format("JsonPathSubject", assertion.Target),
                AssertionKind.Header => Format("HeaderSubject", assertion.Target),
                AssertionKind.ResponseTime => DefaultStrings.Get("Str.Assertions.TimeSubject"),
                _ => DefaultStrings.Get("Str.Assertions.StatusSubject")
            };
            var actual = assertion.Kind == AssertionKind.Header
                ? string.Join(", ", response.Headers.Where(header => string.Equals(header.Key, assertion.Target, StringComparison.OrdinalIgnoreCase)).Select(header => header.Value))
                : (assertion.Kind == AssertionKind.ResponseTime ? response.Elapsed.TotalMilliseconds : (int)response.StatusCode.Value).ToString(CultureInfo.InvariantCulture);
            var label = string.IsNullOrWhiteSpace(assertion.Description)
                ? Format("GeneratedLabel", subject, operatorLabel, assertion.Operator == AssertionOperator.Exists ? "" : assertion.ExpectedValue).TrimEnd()
                : assertion.Description;
            if (assertion.Kind is AssertionKind.Body or AssertionKind.JsonPath or AssertionKind.Schema)
            {
                // A preview prefix can match where the full body would not; never compare against it.
                if (response.BodyWasTruncated)
                {
                    results.Add(AssertionResult.IncompleteFor(assertion.Id, Redact(label), DefaultStrings.Get("Str.Assertions.IncompleteBody")));
                    continue;
                }
                try { actual = Encoding.GetEncoding(string.IsNullOrWhiteSpace(response.CharacterSet) ? "utf-8" : response.CharacterSet).GetString(response.Body); }
                catch (ArgumentException) { actual = Encoding.UTF8.GetString(response.Body); }
            }
            string? failure = null;
            var passed = false;
            if (assertion.Kind == AssertionKind.Schema)
            {
                (passed, actual, failure) = SchemaAssertion.Evaluate(assertion.Target, actual);
            }
            else if (assertion.Kind == AssertionKind.JsonPath)
            {
                (passed, actual, failure) = JsonPathAssertion.Evaluate(assertion, actual);
            }
            else if (assertion.Kind is AssertionKind.Header or AssertionKind.Body)
            {
                if (assertion.Kind == AssertionKind.Header && !response.Headers.Any(header => string.Equals(header.Key, assertion.Target, StringComparison.OrdinalIgnoreCase)))
                    failure = Format("MissingHeader", assertion.Target);
                else
                {
                    try
                    {
                        bool? comparison = assertion.Operator switch
                        {
                            AssertionOperator.Exists when assertion.Kind == AssertionKind.Header => true,
                            AssertionOperator.Equals => actual == assertion.ExpectedValue,
                            AssertionOperator.NotEquals when assertion.Kind == AssertionKind.Header => actual != assertion.ExpectedValue,
                            AssertionOperator.Contains => actual.Contains(assertion.ExpectedValue, StringComparison.Ordinal),
                            AssertionOperator.Matches => Regex.IsMatch(actual, assertion.ExpectedValue, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),
                            _ => null
                        };
                        passed = comparison == true;
                        if (comparison is null)
                            failure = DefaultStrings.Get("Str.Assertions.UnsupportedOperator");
                    }
                    catch (ArgumentException) { failure = Format("InvalidRegex", assertion.ExpectedValue); }
                    catch (RegexMatchTimeoutException) { failure = DefaultStrings.Get("Str.Assertions.RegexTimeout"); }
                }
            }
            else if (assertion.Kind is AssertionKind.StatusCode or AssertionKind.ResponseTime)
            {
                var validNumber = assertion.Kind == AssertionKind.StatusCode
                    ? int.TryParse(assertion.ExpectedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                    : double.TryParse(assertion.ExpectedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number);
                if (!validNumber) failure = Format("InvalidNumber", assertion.ExpectedValue);
                else
                {
                    var expected = double.Parse(assertion.ExpectedValue, CultureInfo.InvariantCulture);
                    var numericActual = assertion.Kind == AssertionKind.ResponseTime ? response.Elapsed.TotalMilliseconds : (int)response.StatusCode.Value;
                    bool? comparison = assertion.Operator switch
                    {
                        AssertionOperator.Equals when assertion.Kind == AssertionKind.StatusCode => numericActual == expected,
                        AssertionOperator.NotEquals when assertion.Kind == AssertionKind.StatusCode => numericActual != expected,
                        AssertionOperator.GreaterThan => numericActual > expected,
                        AssertionOperator.LessThan => numericActual < expected,
                        _ => null
                    };
                    passed = comparison == true;
                    if (comparison is null)
                        failure = DefaultStrings.Get("Str.Assertions.UnsupportedOperator");
                }
            }
            else failure = DefaultStrings.Get("Str.Assertions.UnsupportedKind");
            if (!passed && failure is null) failure = Format("ComparisonMismatch", subject, operatorLabel, assertion.ExpectedValue, actual);
            results.Add(new(assertion.Id, Redact(label), passed,
                Redact(actual), failure is null ? null : Redact(failure)));
        }
        return results;

        string Redact(string value) => _redactor.Redact(value, response.ResolvedSecretValues);
    }

    private static string Format(string key, params object[] values) =>
        string.Format(CultureInfo.InvariantCulture, DefaultStrings.Get("Str.Assertions." + key), values);
}
