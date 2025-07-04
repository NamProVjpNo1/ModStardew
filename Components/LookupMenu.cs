using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Pathoschild.Stardew.Common;
using Pathoschild.Stardew.Common.UI;
using Pathoschild.Stardew.Common.Utilities;
using Pathoschild.Stardew.LookupAnything.Framework.Constants;
using Pathoschild.Stardew.LookupAnything.Framework.Fields;
using Pathoschild.Stardew.LookupAnything.Framework.Lookups;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace Pathoschild.Stardew.LookupAnything.Components;

/// <summary>A UI which shows information about an item.</summary>
internal class LookupMenu : BaseMenu, IScrollableMenu, IDisposable
{
    /*********
    ** Fields
    *********/
    /// <summary>The subject metadata.</summary>
    private readonly ISubject Subject;

    /// <summary>Encapsulates logging and monitoring.</summary>
    private readonly IMonitor Monitor;

    /// <summary>A callback which shows a new lookup for a given subject.</summary>
    private readonly Action<ISubject> ShowNewPage;

    /// <summary>The data to display for this subject.</summary>
    private readonly ICustomField[] Fields;

    /// <summary>The aspect ratio of the page background.</summary>
    private readonly Vector2 AspectRatio = new(Sprites.Letter.Sprite.Width, Sprites.Letter.Sprite.Height);

    /// <summary>Simplifies access to private game code.</summary>
    private readonly IReflectionHelper Reflection;

    /// <summary>The amount to scroll long content on each up/down scroll.</summary>
    private readonly int ScrollAmount;

    /// <summary>Whether the menu should always be full-screen, instead of centered in the window.</summary>
    private readonly bool ForceFullScreen;

    /// <summary>The clickable 'scroll up' icon.</summary>
    private readonly ClickableTextureComponent ScrollUpButton;

    /// <summary>The clickable 'scroll down' icon.</summary>
    private readonly ClickableTextureComponent ScrollDownButton;

    /// <summary>The spacing around the scroll buttons.</summary>
    private readonly int ScrollButtonGutter = 15;

    /// <summary>The blend state to use when rendering the content sprite batch.</summary>
    private readonly BlendState ContentBlendState = new()
    {
        AlphaBlendFunction = BlendFunction.Add,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One,

        ColorBlendFunction = BlendFunction.Add,
        ColorSourceBlend = Blend.SourceAlpha,
        ColorDestinationBlend = Blend.InverseSourceAlpha
    };

    /// <summary>The maximum pixels to scroll.</summary>
    private int MaxScroll;

    /// <summary>The number of pixels to scroll.</summary>
    private int CurrentScroll;

    /// <summary>Whether the game's draw mode has been validated for compatibility.</summary>
    private bool ValidatedDrawMode;

    /// <summary>The pixel areas containing a field which may have clickable links.</summary>
    private readonly Dictionary<ICustomField, Rectangle> LinkableFieldAreas = new(new ObjectReferenceComparer<ICustomField>());

    /// <summary>Whether the game HUD was enabled when the menu was opened.</summary>
    private readonly bool WasHudEnabled;

    /// <summary>Whether to exit the menu on the next update tick.</summary>
    private bool ExitOnNextTick;


    /*********
    ** Public methods
    *********/
    /****
    ** Initialization
    ****/
    /// <summary>Construct an instance.</summary>
    /// <param name="subject">The metadata to display.</param>
    /// <param name="monitor">Encapsulates logging and monitoring.</param>
    /// <param name="reflectionHelper">Simplifies access to private game code.</param>
    /// <param name="scroll">The amount to scroll long content on each up/down scroll.</param>
    /// <param name="showDebugFields">Whether to display debug fields.</param>
    /// <param name="forceFullScreen">Whether the menu should always be full-screen, instead of centered in the window.</param>
    /// <param name="showNewPage">A callback which shows a new lookup for a given subject.</param>
    public LookupMenu(ISubject subject, IMonitor monitor, IReflectionHelper reflectionHelper, int scroll, bool showDebugFields, bool forceFullScreen, Action<ISubject> showNewPage)
    {
        // save data
        this.Subject = subject;
        this.Fields = subject.GetData().Where(p => p.HasValue).ToArray();
        this.Monitor = monitor;
        this.Reflection = reflectionHelper;
        this.ScrollAmount = scroll;
        this.ForceFullScreen = forceFullScreen;
        this.ShowNewPage = showNewPage;
        this.WasHudEnabled = Game1.displayHUD;

        // save debug fields
        if (showDebugFields)
        {
            this.Fields = this.Fields
                .Concat(
                    subject
                        .GetDebugFields()
                        .GroupBy(p =>
                        {
                            if (p.IsPinned)
                                return "debug (pinned)";
                            if (p.OverrideCategory != null)
                                return $"debug ({p.OverrideCategory})";
                            return "debug (raw)";
                        })
                        .OrderByDescending(p => p.Key == "debug (pinned)")
                        .Select(p => (ICustomField)new DataMiningField(p.Key, p))
                )
                .ToArray();
        }

        // add scroll buttons
        this.ScrollUpButton = new ClickableTextureComponent(Rectangle.Empty, CommonSprites.Icons.Sheet, CommonSprites.Icons.UpArrow, 1);
        this.ScrollDownButton = new ClickableTextureComponent(Rectangle.Empty, CommonSprites.Icons.Sheet, CommonSprites.Icons.DownArrow, 1);

        // update layout
        this.UpdateLayout();

        // hide game HUD
        Game1.displayHUD = false;
    }

    /// <inheritdoc />
    public override bool overrideSnappyMenuCursorMovementBan()
    {
        return true; // controller snapping not implemented
    }

    /****
    ** Events
    ****/
    /// <inheritdoc />
    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        this.HandleLeftClick(x, y);
    }

    /// <inheritdoc />
    public override void receiveRightClick(int x, int y, bool playSound = true) { }

    /// <inheritdoc />
    public override void receiveScrollWheelAction(int direction)
    {
        if (direction > 0)    // positive number scrolls content up
            this.ScrollUp();
        else
            this.ScrollDown();
    }

    /// <inheritdoc />
    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        this.UpdateLayout();
    }

    /// <inheritdoc />
    public override void receiveGamePadButton(Buttons button)
    {
        switch (button)
        {
            // left click
            case Buttons.A:
                Point p = Game1.getMousePosition();
                this.HandleLeftClick(p.X, p.Y);
                break;

            // exit
            case Buttons.B:
                this.exitThisMenu();
                break;

            // scroll up
            case Buttons.RightThumbstickUp:
                this.ScrollUp();
                break;

            // scroll down
            case Buttons.RightThumbstickDown:
                this.ScrollDown();
                break;
        }
    }

    /// <inheritdoc />
    public override void update(GameTime time)
    {
        if (this.ExitOnNextTick && this.readyToClose())
            this.exitThisMenu();
        else
            base.update(time);
    }

    /****
    ** Methods
    ****/
    /// <summary>Exit the menu at the next safe opportunity.</summary>
    /// <remarks>This circumvents an issue where the game may freeze in some cases like the load selection screen when the menu is exited at an arbitrary time.</remarks>
    public void QueueExit()
    {
        this.ExitOnNextTick = true;
    }

    /// <inheritdoc />
    public void ScrollUp(int? amount = null)
    {
        this.CurrentScroll -= amount ?? this.ScrollAmount;
    }

    /// <inheritdoc />
    public void ScrollDown(int? amount = null)
    {
        this.CurrentScroll += amount ?? this.ScrollAmount;
    }

    /// <summary>Handle a left-click from the player's mouse or controller.</summary>
    /// <param name="x">The x-position of the cursor.</param>
    /// <param name="y">The y-position of the cursor.</param>
    public void HandleLeftClick(int x, int y)
    {
        // close menu when clicked outside or on close button
        if (!this.isWithinBounds(x, y) || this.upperRightCloseButton.containsPoint(x, y))
            this.exitThisMenu();

        // scroll up or down
        else if (this.ScrollUpButton.containsPoint(x, y))
            this.ScrollUp();
        else if (this.ScrollDownButton.containsPoint(x, y))
            this.ScrollDown();

        // subject links
        else
        {
            foreach ((ICustomField field, Rectangle fieldArea) in this.LinkableFieldAreas)
            {
                if (fieldArea.Contains(x, y) && field.TryGetLinkAt(x, y, out ISubject? subject))
                {
                    this.ShowNewPage(subject);
                    break;
                }
            }
        }
    }

    /// <inheritdoc />
    public override void draw(SpriteBatch b)
    {
        this.Monitor.InterceptErrors("drawing the lookup info", () =>
        {
            this.LinkableFieldAreas.Clear();

            ISubject subject = this.Subject;

            // disable when game is using immediate sprite sorting
            // (This prevents Lookup Anything from creating new sprite batches, which breaks its core rendering logic.
            // Fortunately this very rarely happens; the only known case is the Stardew Valley Fair, when the only thing
            // you can look up anyway is the farmer.)
            if (!this.ValidatedDrawMode)
            {
                IReflectedField<SpriteSortMode> sortModeField = this.Reflection.GetField<SpriteSortMode>(Game1.spriteBatch, "_sortMode");
                if (sortModeField.GetValue() == SpriteSortMode.Immediate)
                {
                    this.Monitor.Log("Aborted the lookup because the game's current rendering mode isn't compatible with the mod's UI. This only happens in rare cases (e.g. the Stardew Valley Fair).", LogLevel.Warn);
                    this.exitThisMenu(playSound: false);
                    return;
                }
                this.ValidatedDrawMode = true;
            }

            // calculate dimensions
            int x = this.xPositionOnScreen;
            int y = this.yPositionOnScreen;
            const int gutter = 15;
            float leftOffset = gutter;
            float topOffset = gutter;
            float contentWidth = this.width - gutter * 2;
            float contentHeight = this.height - gutter * 2;
            int tableBorderWidth = 1;

            // get font
            SpriteFont font = Game1.smallFont;
            float lineHeight = font.MeasureString("ABC").Y;
            float spaceWidth = DrawHelper.GetSpaceWidth(font);

            // draw background
            // (This uses a separate sprite batch because it needs to be drawn before the
            // foreground batch, and we can't use the foreground batch because the background is
            // outside the clipping area.)
            using (SpriteBatch backgroundBatch = new SpriteBatch(Game1.graphics.GraphicsDevice))
            {
                float scale = this.width >= this.height
                    ? this.width / (float)Sprites.Letter.Sprite.Width
                    : this.height / (float)Sprites.Letter.Sprite.Height;

                backgroundBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp);
                backgroundBatch.DrawSprite(Sprites.Letter.Sheet, Sprites.Letter.Sprite, x, y, Sprites.Letter.Sprite.Size, scale: scale);
                backgroundBatch.End();
            }

            // draw foreground
            // (This uses a separate sprite batch to set a clipping area for scrolling.)
            using (SpriteBatch contentBatch = new SpriteBatch(Game1.graphics.GraphicsDevice))
            {
                GraphicsDevice device = Game1.graphics.GraphicsDevice;
                Rectangle prevScissorRectangle = device.ScissorRectangle;
                try
                {
                    // begin draw
                    device.ScissorRectangle = new Rectangle(x + gutter, y + gutter, (int)contentWidth, (int)contentHeight);
                    contentBatch.Begin(SpriteSortMode.Deferred, this.ContentBlendState, SamplerState.PointClamp, null, new RasterizerState { ScissorTestEnable = true });

                    // scroll view
                    this.CurrentScroll = Math.Max(0, this.CurrentScroll); // don't scroll past top
                    this.CurrentScroll = Math.Min(this.MaxScroll, this.CurrentScroll); // don't scroll past bottom
                    topOffset -= this.CurrentScroll; // scrolled down == move text up

                    // draw portrait
                    if (subject.DrawPortrait(contentBatch, new Vector2(x + leftOffset, y + topOffset), new Vector2(70, 70)))
                        leftOffset += 72;

                    // draw fields
                    float wrapWidth = this.width - leftOffset - gutter;
                    {
                        // draw name & item type
                        {
                            Vector2 nameSize = contentBatch.DrawTextBlock(font, $"{subject.Name}.", new Vector2(x + leftOffset, y + topOffset), wrapWidth, bold: Constant.AllowBold);
                            Vector2 typeSize = subject.Type != null
                                ? contentBatch.DrawTextBlock(font, $"{subject.Type}.", new Vector2(x + leftOffset + nameSize.X + spaceWidth, y + topOffset), wrapWidth)
                                : Vector2.Zero;
                            topOffset += Math.Max(nameSize.Y, typeSize.Y);
                        }

                        // draw description
                        if (subject.Description != null)
                        {
                            Vector2 size = contentBatch.DrawTextBlock(font, subject.Description?.Replace(Environment.NewLine, " "), new Vector2(x + leftOffset, y + topOffset), wrapWidth);
                            topOffset += size.Y;
                        }

                        // draw spacer
                        topOffset += lineHeight;

                        // draw custom fields
                        if (this.Fields.Any())
                        {
                            ICustomField[] fields = this.Fields;
                            float cellPadding = 3;
                            float labelWidth = fields.Where(p => p.HasValue).Max(p => font.MeasureString(p.Label).X);
                            float valueWidth = wrapWidth - labelWidth - cellPadding * 4 - tableBorderWidth;
                            foreach (ICustomField field in fields)
                            {
                                if (!field.HasValue)
                                    continue;

                                // draw label
                                Vector2 labelSize = contentBatch.DrawTextBlock(font, field.Label, new Vector2(x + leftOffset + cellPadding, y + topOffset + cellPadding), wrapWidth);

                                // draw value
                                Vector2 valuePosition = new Vector2(x + leftOffset + labelWidth + cellPadding * 3, y + topOffset + cellPadding);
                                Vector2 valueSize;
                                if (field.ExpandLink is not null)
                                    valueSize = contentBatch.DrawTextBlock(font, field.ExpandLink.Value, valuePosition, valueWidth);
                                else
                                {
                                    valueSize =
                                        field.DrawValue(contentBatch, font, valuePosition, valueWidth)
                                        ?? contentBatch.DrawTextBlock(font, field.Value, valuePosition, valueWidth);
                                }

                                // draw table row
                                Vector2 rowSize = new Vector2(labelWidth + valueWidth + cellPadding * 4, Math.Max(labelSize.Y, valueSize.Y));
                                Color lineColor = Color.Gray;
                                contentBatch.DrawLine(x + leftOffset, y + topOffset, new Vector2(rowSize.X, tableBorderWidth), lineColor); // top
                                contentBatch.DrawLine(x + leftOffset, y + topOffset + rowSize.Y, new Vector2(rowSize.X, tableBorderWidth), lineColor); // bottom
                                contentBatch.DrawLine(x + leftOffset, y + topOffset, new Vector2(tableBorderWidth, rowSize.Y), lineColor); // left
                                contentBatch.DrawLine(x + leftOffset + labelWidth + cellPadding * 2, y + topOffset, new Vector2(tableBorderWidth, rowSize.Y), lineColor); // middle
                                contentBatch.DrawLine(x + leftOffset + rowSize.X, y + topOffset, new Vector2(tableBorderWidth, rowSize.Y), lineColor); // right

                                // track link area
                                if (field is { MayHaveLinks: true, HasValue: true })
                                    this.LinkableFieldAreas[field] = new Rectangle((int)valuePosition.X, (int)valuePosition.Y, (int)valueSize.X, (int)valueSize.Y);

                                // update offset
                                topOffset += Math.Max(labelSize.Y, valueSize.Y);
                            }
                        }
                    }

                    // update max scroll
                    this.MaxScroll = Math.Max(0, (int)(topOffset - contentHeight + this.CurrentScroll));

                    // draw scroll icons
                    if (this.MaxScroll > 0 && this.CurrentScroll > 0)
                        this.ScrollUpButton.draw(b);
                    if (this.MaxScroll > 0 && this.CurrentScroll < this.MaxScroll)
                        this.ScrollDownButton.draw(b);

                    // end draw
                    contentBatch.End();
                }
                catch (ArgumentException ex) when (!BaseMenu.UseSafeDimensions && ex.ParamName == "value" && ex.StackTrace?.Contains("Microsoft.Xna.Framework.Graphics.GraphicsDevice.set_ScissorRectangle") == true)
                {
                    this.Monitor.Log("The viewport size seems to be inaccurate. Enabling compatibility mode; lookup menu may be misaligned.", LogLevel.Warn);
                    this.Monitor.Log(ex.ToString());
                    BaseMenu.UseSafeDimensions = true;
                    this.UpdateLayout();
                }
                finally
                {
                    device.ScissorRectangle = prevScissorRectangle;
                }
            }

            // draw close button
            this.upperRightCloseButton.draw(b);

            // draw cursor
            this.drawMouse(Game1.spriteBatch);
        }, this.OnDrawError);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.ContentBlendState.Dispose();

        this.CleanupImpl();
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Update the layout dimensions based on the current game scale.</summary>
    private void UpdateLayout()
    {
        Point viewport = this.GetViewportSize();

        // update size & position
        if (this.ForceFullScreen)
        {
            this.xPositionOnScreen = 0;
            this.yPositionOnScreen = 0;
            this.width = viewport.X;
            this.height = viewport.Y;
        }
        else
        {
            this.width = Math.Min(Game1.tileSize * 20, viewport.X);
            this.height = Math.Min((int)(this.AspectRatio.Y / this.AspectRatio.X * this.width), viewport.Y);

            Vector2 origin = new Vector2(viewport.X / 2 - this.width / 2, viewport.Y / 2 - this.height / 2); // derived from Utility.getTopLeftPositionForCenteringOnScreen, adjusted to account for possibly different GPU viewport size
            this.xPositionOnScreen = (int)origin.X;
            this.yPositionOnScreen = (int)origin.Y;
        }

        // update up/down buttons
        int x = this.xPositionOnScreen;
        int y = this.yPositionOnScreen;
        int gutter = this.ScrollButtonGutter;
        float contentHeight = this.height - gutter * 2;
        this.ScrollUpButton.bounds = new Rectangle(x + gutter, (int)(y + contentHeight - CommonSprites.Icons.UpArrow.Height - gutter - CommonSprites.Icons.DownArrow.Height), CommonSprites.Icons.UpArrow.Height, CommonSprites.Icons.UpArrow.Width);
        this.ScrollDownButton.bounds = new Rectangle(x + gutter, (int)(y + contentHeight - CommonSprites.Icons.DownArrow.Height), CommonSprites.Icons.DownArrow.Height, CommonSprites.Icons.DownArrow.Width);

        // add close button
        this.initializeUpperRightCloseButton();
    }

    /// <summary>The method invoked when an unhandled exception is intercepted.</summary>
    /// <param name="ex">The intercepted exception.</param>
    private void OnDrawError(Exception ex)
    {
        this.Monitor.InterceptErrors("handling an error in the lookup code", () => this.exitThisMenu());
    }

    /// <inheritdoc />
    protected override void cleanupBeforeExit()
    {
        this.CleanupImpl();
        base.cleanupBeforeExit();
    }

    /// <summary>Perform cleanup specific to the lookup menu.</summary>
    private void CleanupImpl()
    {
        Game1.displayHUD = this.WasHudEnabled;
    }
}
