using System.Text.Json;
using System.Text.Json.Serialization;
using Snail.MCP.Plans.Core;

namespace Snail.MCP.Plans.Tools;

/// <summary>What the tools take as JSON, described by generated code rather than found by reflection.</summary>
/// <remarks>
/// A native build cannot look a type up at run time, and neither can this server built any other way: with
/// PublishAot on, reflection-based serialization is off everywhere, so a type missing here fails the same in a test
/// as in the published program — when the tool is listed, not when the build ends. Statuses and change kinds travel
/// by name, as they always have.
/// </remarks>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(StepStatus))]
[JsonSerializable(typeof(StepChange[]))]
public sealed partial class PlansJson : JsonSerializerContext;
