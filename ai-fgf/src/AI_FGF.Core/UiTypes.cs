namespace AI_FGF;

/// <summary>A single validation failure.</summary>
public sealed record FieldError(string Key, string Message);

/// <summary>Which edge a button bar aligns its buttons to.</summary>
public enum BarAlign { Left, Right }
