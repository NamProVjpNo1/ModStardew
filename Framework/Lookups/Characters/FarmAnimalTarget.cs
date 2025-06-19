using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace Pathoschild.Stardew.LookupAnything.Framework.Lookups.Characters;

/// <summary>Positional metadata about a farm animal.</summary>
internal class FarmAnimalTarget : GenericTarget<FarmAnimal>
{
    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="gameHelper">Provides utility methods for interacting with the game code.</param>
    /// <param name="value">The underlying in-game entity.</param>
    /// <param name="tilePosition">The object's tile position in the current location (if applicable).</param>
    /// <param name="getSubject">Get the subject info about the target.</param>
    public FarmAnimalTarget(GameHelper gameHelper, FarmAnimal value, Vector2 tilePosition, Func<ISubject> getSubject)
        : base(gameHelper, SubjectType.FarmAnimal, value, tilePosition, getSubject) { }

    /// <inheritdoc />
    public override Rectangle GetSpritesheetArea()
    {
        return this.Value.Sprite.SourceRect;
    }

    /// <inheritdoc />
    public override Rectangle GetWorldArea()
    {
        return this.GetSpriteArea(this.Value.GetBoundingBox(), this.GetSpritesheetArea());
    }

    /// <inheritdoc />
    public override bool SpriteIntersectsPixel(Vector2 tile, Vector2 position, Rectangle spriteArea)
    {
        SpriteEffects spriteEffects = this.Value.flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        return this.SpriteIntersectsPixel(tile, position, spriteArea, this.Value.Sprite.Texture, this.GetSpritesheetArea(), spriteEffects);
    }
}
