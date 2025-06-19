using System;
using Microsoft.Xna.Framework;
using StardewValley.TerrainFeatures;

namespace Pathoschild.Stardew.LookupAnything.Framework.Lookups.Items;

/// <summary>Positional metadata about placed flooring.</summary>
internal class FlooringTarget : GenericTarget<Flooring>
{
    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="gameHelper">Provides utility methods for interacting with the game code.</param>
    /// <param name="value">The underlying in-game entity.</param>
    /// <param name="tilePosition">The object's tile position in the current location (if applicable).</param>
    /// <param name="getSubject">Get the subject info about the target.</param>
    public FlooringTarget(GameHelper gameHelper, Flooring value, Vector2 tilePosition, Func<ISubject> getSubject)
        : base(gameHelper, SubjectType.Object, value, tilePosition, getSubject)
    {
        this.Precedence = GenericTarget<Flooring>.PrecedenceForFlooring;
    }

    /// <inheritdoc />
    public override Rectangle GetSpritesheetArea()
    {
        return Rectangle.Empty;
    }
}
