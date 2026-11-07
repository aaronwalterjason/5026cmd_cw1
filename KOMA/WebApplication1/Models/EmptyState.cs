namespace KOMA.Models;

// Data for the shared "nothing here yet" display (Pages/Shared/_Empty.cshtml).
// Art = bowl | tag | table | notebook | calendar | floor | search | people.
public record EmptyState(string Art, string Title, string? Text = null, string? ActionText = null, string? ActionUrl = null, bool Compact = false);
