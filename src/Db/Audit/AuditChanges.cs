using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DashboardStarter.Db.Audit;

/// <summary>
/// The changed fields of one audit entry, and the JSON they're stored as:
/// <c>{"name":{"old":"Acme","new":"Acme Ltd"},"phone":{"old":null,"new":"+1 555 0100"}}</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the allow-list. A field is recorded only when code calls <see cref="Add"/> for it,
/// by name; nothing copies a whole row in. Never add a secret: a password hash, a token, a
/// reset code.
/// </para>
/// <para>
/// <see cref="Add"/> skips a field whose old and new values are the same, so an entry holds only
/// what changed, and <see cref="IsEmpty"/> answers "did this save change anything?". Values
/// compare exactly (ordinal): a change of capitalization is a change.
/// </para>
/// </remarks>
public sealed class AuditChanges
{
  private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

  // In the order they were added, which is the order the history shows them in.
  private readonly List<KeyValuePair<string, AuditFieldChange>> _fields = [];

  public bool IsEmpty => _fields.Count == 0;

  /// <summary>Records a field, if it changed. <paramref name="field"/> is the name the API uses for it ("name", "status").</summary>
  public AuditChanges Add(string field, string? oldValue, string? newValue)
  {
    if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
    {
      _fields.Add(new KeyValuePair<string, AuditFieldChange>(field, new AuditFieldChange(oldValue, newValue)));
    }

    return this;
  }

  public string ToJson()
  {
    return JsonSerializer.Serialize(new Dictionary<string, AuditFieldChange>(_fields), JsonOptions);
  }

  /// <summary>Reads the Changes column back. Null stays null.</summary>
  public static IReadOnlyDictionary<string, AuditFieldChange>? Parse(string? json)
  {
    return json is null ? null : JsonSerializer.Deserialize<Dictionary<string, AuditFieldChange>>(json, JsonOptions);
  }
}

/// <summary>One field's change. <paramref name="Old"/> is null on a Created entry, and whenever the field was empty.</summary>
public sealed record AuditFieldChange(string? Old, string? New);
