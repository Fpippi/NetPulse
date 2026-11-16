namespace NetPulse.Core.Appearance;

/// <summary>A dashboard colour theme: a stable id (used in CSS and the cookie) and a display name.</summary>
public sealed record Theme(string Id, string Name);
