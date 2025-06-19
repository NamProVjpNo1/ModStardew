namespace Pathoschild.Stardew.LookupAnything.Framework.Data;

/// <summary>A loot entry parsed from the game data.</summary>
/// <param name="ItemId">The item's unqualified item ID.</param>
/// <param name="MinDrop">The minimum number to drop.</param>
/// <param name="MaxDrop">The maximum number to drop.</param>
/// <param name="Probability">The probability that the item will be dropped.</param>
/// <param name="Conditions">The raw game state queries which indicate whether this drop applies.</param>
internal record ItemDropData(string ItemId, int MinDrop, int MaxDrop, float Probability, string? Conditions = null);
