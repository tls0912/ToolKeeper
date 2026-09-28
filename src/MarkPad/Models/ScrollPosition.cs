namespace MarkPad.Models;

/// <summary>A fractional, one-based source line plus viewport progress for document boundaries.</summary>
public sealed record ScrollPosition(double Line, double Progress);
