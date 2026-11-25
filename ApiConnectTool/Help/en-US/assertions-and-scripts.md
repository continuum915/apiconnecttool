---
id: assertions-and-scripts
title: Assertions and Scripts
order: 6
---

# Assertions and scripts

## Add a response check

Before sending, choose **Request > Inspect Variables...** to inspect the current request's saved-scope resolution, including inherited authorization. It shows defaults, overrides and shadowed definitions without running scripts. Pre-request scripts and collection runner data may override that preview at execution time; run and iteration values are not fabricated by the inspector. See [Environments and Variables](environments).

1. Open the request's **Assertions** tab and click **Add Assertion**.
2. Choose **Status Code**, **Header**, **Response Time**, **Body**, **JSON Path** or **Schema** in **Kind**.
3. Set **Operator** and **Expected Value**. Header and JSON Path checks also need a **Target**, such as `Content-Type` or `$.id`. Response time uses milliseconds.
4. Add a **Description** to name the check. Clear **Enabled** to skip it.
5. Click **Send**, then open the response's **Test Results** tab. Each row shows its result, actual value and failure explanation. A failed check does not change the HTTP response status.

For example, a Status Code check with Equals and Expected Value `200` verifies a successful response. Checks and scripts read the original decoded response body, including its whitespace. Pretty and Raw only change the viewer.

## Schema assertions

In the request's Assertions tab, add an assertion, select **Schema**, and paste
a JSON schema object into **Target** (double-click the cell to edit). Multiline
JSON is accepted. Operator and Expected value are unused for this kind.
A description is optional. After sending, Test Results shows whether the
response body matches and reports the first failing location and reason.

ApiConnectTool supports this practical subset:

| Keyword | Accepted value | What it checks |
|---|---|---|
| `type` | One of `object`, `array`, `string`, `number`, `integer`, `boolean`, `null` | The JSON value's type; integer includes numbers such as `1.0` and `1e2` with no fractional value. |
| `properties` | Object mapping property names to schema objects | Present properties are checked against their schemas; absent properties are allowed unless required. |
| `required` | Array of property-name strings | Each named property must exist on an object. A present null value counts as present. |
| `enum` | Nonempty array of JSON values | The value must equal an entry structurally; object property order does not matter and equivalent JSON numbers compare equally. |
| `items` | A schema object | Every element of an array must match this schema. Empty arrays pass. |

Schemas can nest objects and arrays to any depth, subject to available memory.
An empty schema object accepts any valid JSON value. Object keywords apply
only to objects and `items` only to arrays; add `type` to require that shape.
Property names and strings are case-sensitive.

All other keywords are ignored, including `$ref`, `$defs`, `format`, `pattern`,
`minimum`, `maximum`, `additionalProperties`, `oneOf`, and `allOf`.
This is not full JSON Schema validation. Type arrays, boolean schemas, and
tuple arrays for `items` are not supported and produce an invalid-schema result.

For example, this schema requires an array of user objects with an integer
identifier and one of two roles:

```json
{
  "type": "object",
  "required": ["users"],
  "properties": {
    "users": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["id", "role"],
        "properties": {
          "id": { "type": "integer" },
          "role": { "enum": ["reader", "editor"] }
        }
      }
    }
  }
}
```

A wrong identifier type reports a location such as `$["users"][0]["id"]`.
Invalid schema JSON or malformed supported keywords fail with an explanation,
including in schemas for absent properties or empty arrays. Non-JSON response
bodies also fail with an explanation. Results redact resolved secrets after
comparison.

## Request scripts

The Scripts tab saves a pre-request script and a test script with each request.
Open the **Scripts** tab, enter code in **Pre-request script** or **Test script**, save the request and click **Send**. Inspect script checks in **Test Results**.
Pre-request scripts run before variable substitution; a script error stops the send.
Test scripts run after a successful response, followed by declarative assertions.
Test Results lists script tests first. Downloads do not run scripts.

```javascript
pm.variables.set("timestamp", String(Date.now()));
pm.test("has id", () => pm.expect(pm.response.json().id).toExist());
```

`pm.variables.get(name)` resolves run variables, then the active collection environment,
the active global environment, collection variables and Globals.
`pm.variables.set(name, value)` writes an in-memory run variable, visible to this send
and later requests in the same iteration. It does not change saved scopes.

`pm.environment`, `pm.collectionVariables`, and `pm.globals` each provide `get(name)`,
`set(name, value)`, `has(name)`, and `unset(name)`. These APIs work in both pre-request
and test scripts. `set` updates the current value, preserving the secret flag of an
existing variable. New variables are not secret. `unset` removes the variable.
`pm.environment.get` reads the active collection environment first, then the active global
environment. `pm.environment.set` and `unset` write to the active collection environment
when one is selected, and otherwise to the active global environment.
Scope changes are saved when a send or collection run finishes, including failed sends.
Writing an unavailable scope (neither environment selected, or no owning collection) fails
the script with a message naming the unavailable scope.

For example, a login request can save a token for the next request:

```javascript
pm.environment.set("token", pm.response.json().access_token);
```

Mark `token` as secret in the environment editor before sending to protect its values
in history, generated cURL, script results, and run reports.

`pm.request` is a read-only view of method, URL, headers, and body.
Test scripts also receive `pm.response.code` and `pm.response.status` (both the
numeric status), `headers`, `body`, `text()`, `json()`, and `responseTime`
(milliseconds). `json()` throws a script-catchable error for invalid JSON. Header maps
preserve their original names.

`pm.test(name, fn)` records a named result and continues after a failed callback. A
top-level script error is a failed result and still allows declarative assertions to run
after a successful send.

### Assertions in scripts

The most common Postman test expressions run unchanged:

```javascript
pm.test("created", () => pm.response.to.have.status(201));
pm.test("json", () => pm.response.to.have.header("Content-Type"));
pm.test("id", () => pm.expect(pm.response.json()).to.have.property("id", 7));
pm.test("fast", () => pm.expect(pm.response.responseTime).to.be.below(500));
```

`pm.expect(value)` supports these Chai-style members:

| Member | Passes when |
| --- | --- |
| `to.equal(x)` (also `equals`, `eq`) | the value is strictly equal (`===`) to `x`. |
| `to.eql(x)` | the value is deeply equal to `x`: same keys and values, compared recursively. |
| `to.include(x)` (also `includes`, `contain`, `contains`) | a string contains the text `x`, an array has an element `=== x`, or an object has every key and value of the object `x`. |
| `to.have.property(name)` or `(name, value)` | the value has the property, and its value is `=== value` when given. |
| `to.be.above(n)` (also `gt`, `greaterThan`) | the value is a number greater than the number `n`. |
| `to.be.below(n)` (also `lt`, `lessThan`) | the value is a number less than the number `n`. |
| `to.exist` | the value is neither `null` nor `undefined`. |
| `to.be.ok` | the value is truthy. |

`pm.response.to` supports:

| Member | Passes when |
| --- | --- |
| `have.status(n)` | the status code is `n`. |
| `have.header(name)` or `(name, value)` | the header exists, matched without regard to case, and has exactly `value` when given. |
| `have.body()` or `have.body(text)` | the body is not empty, or is exactly `text`. |
| `be.ok` | the status is 200. |
| `be.success` | the status is from 200 to 299. |

Put `.not` anywhere in a chain to negate the members after it, as in
`pm.response.to.not.have.status(500)`. The words `to`, `be`, `been`, `is`, `that`,
`which`, `and`, `has`, `have`, `with`, `at`, `of`, `same`, `but`, `does`, `still` and
`also` only make a chain read naturally. Members can be chained, as in
`pm.expect(n).to.be.above(1).and.below(10)`. A failed check reports what was expected,
for example `Expected 404 to equal 200.`

ApiConnectTool's earlier matchers `toEqual`, `toContain`, `toBeGreaterThan`, `toBeLessThan` and
`toExist()` keep working.

Differences from Postman:

- Only the members above are available. Any other member, such as `to.be.a("string")`,
  `to.be.true`, `to.have.lengthOf(n)` or `pm.response.to.have.jsonBody()`, fails that test
  with a message that names it as not supported. Rewrite it with a supported member, for
  example `pm.expect(typeof v).to.equal("string")` or `pm.expect(list.length).to.equal(3)`.
- `pm.response.status` is the numeric status code, the same as `pm.response.code`. In
  Postman it is the status text, such as "OK".
- `above` and `below` fail when either side is not a number, even with `.not`.
- `have.property` does not change the subject to the property's value for the rest of the
  chain.

If the response body was truncated, any check that reads it, including `have.body`,
`pm.response.json()` and `pm.response.text()`, is reported as incomplete, never as passed.

### Sandbox limits

| Resource | Limit |
|---|---|
| Wall-clock execution | 2,000 ms default; configurable by runner callers from 1 to 10,000 ms |
| Recursion | 200 calls |
| Memory allocations | 16 MB (16,777,216 bytes) |
| Executed statements | 50,000 |
| Host capabilities | Only the `pm` context; no CLR, reflection, files, network, processes, or module loader |

Each invocation uses a fresh Jint engine. No CLR objects or host callbacks are
exposed. String code compilation (`eval` and function constructors) is disabled.
Limit failures are contained and returned as timed out or limit exceeded.
Errors and test results redact resolved secret values, including overwritten
versions of secret variables. Standard JavaScript built-ins remain available.

## Script console

Scripts can call `console.log`, `console.info`, `console.warn`, `console.error` and `console.debug`. Arguments are joined with spaces; objects and arrays are shown as JSON. The output appears in a **Console** section of the **Test Results** tab, pre-request lines first, each with its level.

Each script keeps at most 200 lines or 16 KiB of output; anything after that is dropped and a single "output truncated" line is shown. Known secret values are replaced with a placeholder, including values a later script wrote. Output is kept only for the last send and is never saved. The [Console](console) also collects script output from every send and collection run in this session.

## Collection runs

Right-click a collection and choose **Run Collection...**, or use
**Collection > Run Collection...**. Select the
collection environment and global environment (the current selections are the defaults), then Start. Requests follow
the navigator's order, sharing the application's cookie jar and passing script
variables to the next request. Production confirmation follows the workspace's
warning mode and happens once before any requests are sent.

The window shows progress, results, and run/pass/fail/error totals with elapsed
time. Cancel or close the window to stop an active send and preserve results
already gathered. An interrupted send is recorded as an errored entry; requests
not yet reached are not counted. Writes to environment, collection and global current values are saved when the run finishes; run variables remain temporary. See [Collection Runner](collection-runner) for folder runs and iteration data.

Return to [Contents](contents).
