// Anniversary Wall — ASP.NET Core shared-storage backend for Azure App Service.
//
//   GET  /api/wall  -> returns the current shared state (JSON)
//   POST /api/wall  -> merges the posted client state into shared storage, returns merged state
//
// The JSON contract matches the client's buildState():
//   { posts:[...], users:{...}, banner:{...}, admin:"", gifKey:"", fx:"", scalarsAt:0 }
//
// The merge engine is a faithful C# port of netlify/functions/wall.mjs. It uses
// System.Text.Json's JsonNode/JsonObject so arbitrary post/user/banner fields are
// preserved generically (no strongly-typed POCOs that would silently drop fields).

using System.Text.Json;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// ----- Static front-end: serve wwwroot/index.html -----
app.UseDefaultFiles();   // maps "/" -> /index.html
app.UseStaticFiles();

// ----- Persistent state file -----
// On Azure App Service the writable, persistent area is the HOME directory:
//   Linux:   /home   Windows: D:\home  (exposed via the HOME env var)
// Files under the content root can be wiped on redeploy, so we store state under
// HOME/data when HOME is set, and fall back to App_Data locally.
string home = Environment.GetEnvironmentVariable("HOME") ?? "";
string dataDir = !string.IsNullOrEmpty(home)
    ? Path.Combine(home, "data")
    : Path.Combine(app.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDir);
string statePath = Path.Combine(dataDir, "state.json");

// One writer at a time — App Service can serve concurrent requests on one instance.
var gate = new SemaphoreSlim(1, 1);

var jsonOpts = new JsonSerializerOptions { WriteIndented = false };

string[] reactionKeys = { "cheers", "applause", "grateful", "celebrate" };

JsonObject EmptyState() => new()
{
    ["posts"] = new JsonArray(),
    ["users"] = new JsonObject(),
    ["banner"] = null,
    ["admin"] = "",
    ["gifKey"] = "",
    ["fx"] = "",
    ["scalarsAt"] = 0
};

JsonObject ReadState()
{
    if (!File.Exists(statePath)) return EmptyState();
    try
    {
        string raw = File.ReadAllText(statePath);
        if (string.IsNullOrWhiteSpace(raw)) return EmptyState();
        return JsonNode.Parse(raw) as JsonObject ?? EmptyState();
    }
    catch
    {
        return EmptyState();
    }
}

void WriteState(JsonObject state)
{
    // Write to a temp file then move, so a crash mid-write can't corrupt state.json.
    string tmp = statePath + ".tmp";
    File.WriteAllText(tmp, state.ToJsonString(jsonOpts));
    File.Move(tmp, statePath, overwrite: true);
}

// ---------- merge engine (ported from wall.mjs) ----------

long NumberOf(JsonNode? n)
{
    if (n is JsonValue v)
    {
        if (v.TryGetValue(out long l)) return l;
        if (v.TryGetValue(out double d)) return (long)d;
    }
    return 0;
}

long PostStamp(JsonObject p) => Math.Max(NumberOf(p["editedAt"]), NumberOf(p["createdAt"]));

JsonObject UnionReactions(JsonObject? ra, JsonObject? rb)
{
    var outObj = new JsonObject();
    foreach (var k in reactionKeys)
    {
        var seen = new HashSet<string>();
        var ordered = new List<string>();
        void take(JsonObject? src)
        {
            if (src?[k] is JsonArray arr)
                foreach (var item in arr)
                {
                    var name = item?.GetValue<string>() ?? "";
                    if (seen.Add(name)) ordered.Add(name);
                }
        }
        take(ra);
        take(rb);
        var outArr = new JsonArray();
        foreach (var name in ordered) outArr.Add(name);
        outObj[k] = outArr;
    }
    return outObj;
}

JsonObject Clone(JsonObject o) => JsonNode.Parse(o.ToJsonString())!.AsObject();

JsonObject MergePost(JsonObject x, JsonObject y)
{
    var newer = PostStamp(y) >= PostStamp(x) ? y : x;
    var outObj = Clone(newer);
    bool xDel = x["deleted"]?.GetValue<bool>() ?? false;
    bool yDel = y["deleted"]?.GetValue<bool>() ?? false;
    outObj["deleted"] = xDel || yDel;
    outObj["reactions"] = UnionReactions(x["reactions"] as JsonObject, y["reactions"] as JsonObject);
    return outObj;
}

JsonArray MergePosts(JsonArray? a, JsonArray? b)
{
    var byId = new Dictionary<string, JsonObject>();
    void ingest(JsonArray? src, bool merge)
    {
        if (src == null) return;
        foreach (var node in src)
        {
            if (node is not JsonObject p) continue;
            var id = p["id"]?.GetValue<string>() ?? "";
            if (id.Length == 0) continue;
            byId[id] = (merge && byId.TryGetValue(id, out var prev)) ? MergePost(prev, p) : p;
        }
    }
    ingest(a, false);
    ingest(b, true);

    var list = byId.Values.ToList();
    list.Sort((x, y) => NumberOf(y["createdAt"]).CompareTo(NumberOf(x["createdAt"])));
    var outArr = new JsonArray();
    foreach (var p in list) outArr.Add(Clone(p));
    return outArr;
}

JsonObject MergeUsers(JsonObject? a, JsonObject? b, long aAt, long bAt)
{
    var outObj = new JsonObject();
    if (a != null)
        foreach (var kv in a) outObj[kv.Key] = kv.Value is null ? null : Clone(kv.Value.AsObject());
    if (b != null)
        foreach (var kv in b)
            if (!outObj.ContainsKey(kv.Key) || bAt >= aAt)
                outObj[kv.Key] = kv.Value is null ? null : Clone(kv.Value.AsObject());
    return outObj;
}

JsonNode? FirstTruthy(params JsonNode?[] candidates)
{
    foreach (var c in candidates)
    {
        if (c is null) continue;
        if (c is JsonValue v && v.TryGetValue(out string? s) && string.IsNullOrEmpty(s)) continue;
        return c.DeepClone();
    }
    return null;
}

JsonObject MergeState(JsonObject local, JsonObject remote)
{
    long lAt = NumberOf(local["scalarsAt"]);
    long rAt = NumberOf(remote["scalarsAt"]);
    var w = rAt >= lAt ? remote : local;

    return new JsonObject
    {
        ["posts"] = MergePosts(local["posts"] as JsonArray, remote["posts"] as JsonArray),
        ["users"] = MergeUsers(local["users"] as JsonObject, remote["users"] as JsonObject, lAt, rAt),
        ["banner"] = FirstTruthy(w["banner"], local["banner"], remote["banner"]),
        ["admin"] = FirstTruthy(w["admin"], local["admin"], remote["admin"]) ?? "",
        ["gifKey"] = FirstTruthy(w["gifKey"], local["gifKey"], remote["gifKey"]) ?? "",
        ["fx"] = FirstTruthy(w["fx"], local["fx"], remote["fx"]) ?? "",
        ["scalarsAt"] = Math.Max(lAt, rAt)
    };
}

// ---------- API ----------

app.MapGet("/api/wall", async (HttpResponse res) =>
{
    await gate.WaitAsync();
    try
    {
        var current = ReadState();
        res.Headers["cache-control"] = "no-store";
        return Results.Text(current.ToJsonString(jsonOpts), "application/json");
    }
    finally { gate.Release(); }
});

app.MapPost("/api/wall", async (HttpRequest req, HttpResponse res) =>
{
    JsonObject? incoming;
    try
    {
        using var doc = await JsonDocument.ParseAsync(req.Body);
        incoming = JsonNode.Parse(doc.RootElement.GetRawText()) as JsonObject;
    }
    catch
    {
        return Results.Json(new { error = "invalid json" }, statusCode: 400);
    }
    if (incoming == null)
        return Results.Json(new { error = "invalid payload" }, statusCode: 400);

    await gate.WaitAsync();
    try
    {
        var current = ReadState();
        var merged = MergeState(current, incoming);
        WriteState(merged);
        res.Headers["cache-control"] = "no-store";
        return Results.Text(merged.ToJsonString(jsonOpts), "application/json");
    }
    finally { gate.Release(); }
});

app.Run();
