using System;
using AlloyClient.Utils;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;

namespace AlloyClient.Screens.Components.Containers;

// The little show on the LEFT page of the sign-in book (2026-09-21): a Wizard walks in from the page's edge, stops in the middle, turns to face
// you and talks in the game's own speech bubbles - one set of lines while you register, another while you sign in - and between lines wanders
// left and right or flicks a spell (the attack frame with a small recoil, no projectile). Same frame tricks as the title screen's cave battle
// (CaveBattle.RenderHero): players sheet, FaceRight 0 = idle, 1-2 = walk, 5 = the double-width attack frame, FaceDown for looking at you.
// Everything is timed by its own EnterFrame clock, restarts whenever the page is shown again, and stays inside the page's writable area.
public sealed class BookActor : Container {

    public enum Mood { Register, SignIn }

    private static readonly string[] RegisterLines = [
        "Thanks for joining the fight, fellow warrior!",
        "A new name for the realm. Pick a good one!",
        "Every legend starts with a sign-up.",
        "The Nexus could use another pair of hands.",
        "Your name goes on the Portal forever. No pressure.",
        "Warrior or Wizard? You can be both, later."
    ];

    private static readonly string[] SignInLines = [
        "Welcome back mate, we thought we lost you.",
        "The Nexus hasn't been the same without you.",
        "Your gear is right where you left it.",
        "Took you long enough. Let's go.",
        "Good to see you again, friend.",
        "The realm kept your seat warm."
    ];

    private const int WizardArt = 0;                 // players sheet block 0 = the Wizard
    private const int SpriteSize = 104;              // the 32x32 cell drawn this big (the art fills about half of it)
    private const float FootInset = SpriteSize * 9f / 32f;
    private const int WalkFrameMs = 190;
    private const float WalkSpeed = 0.075f;           // px per ms
    private const int IdleAttackFrame = 5;

    private const int TalkMs = 3600;
    private const int BetweenMs = 900;
    private const int CastMs = 700;
    private const int LookMs = 1200;

    private const uint BubbleBack = 0xE1DFDC, BubbleEdge = 0xFFFFFF, BubbleText = 0x545454;
    private const int BubbleCut = 10, BubbleOutline = 2, BubbleFont = 15, BubbleMaxWidth = 150;
    private const int BubbleGap = 6;

    private enum Act { EnterWalk, Look, Talk, Wander, Cast, Rest }

    private readonly Mood _mood;
    private readonly int _left, _right, _top, _bottom;      // the page area this actor lives in (its own coordinates = the book's)
    private readonly ObjectRect _body;
    private Container _bubble;
    private readonly Random _rng = new();

    private Act _act;
    private double _actStart;
    private double _ms;
    private float _x, _y;
    private float _targetX;
    private int _facing = 1;               // +1 right, -1 left
    private bool _lookingAtYou;
    private int _shownFrame = -1;
    private bool _shownDown;
    private int _lastLine = -1;

    public BookActor(Mood mood, int left, int right, int top, int bottom) {
        _mood = mood;
        _left = left; _right = right; _top = top; _bottom = bottom;

        _body = new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.Create(Main.Atlas.GetAnimationAtlasData("players", WizardArt).FaceRight[0], TextureType.GameAtlas),
            Width = SpriteSize,
            Height = SpriteSize,
            Anchor = UiAnchor.Middle,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        AddChild(_body);

        AddEventListener(Event.AddedToStage, Restart);
        AddEventListener(Event.EnterFrame, OnFrame);
        Restart();
    }

    // The floor line the actor walks on: a little above the bottom of the page.
    private float FloorY => _bottom - 26;

    private void Restart() {
        _ms = 0;
        _x = _left + SpriteSize / 2f - 10;     // at the page's left edge (nothing may be drawn over the book art beside the page)
        _y = FloorY;
        _facing = 1;
        _lookingAtYou = false;
        _targetX = (_left + _right) / 2f;
        HideBubble();
        Begin(Act.EnterWalk);
    }

    private void Begin(Act act) {
        _act = act;
        _actStart = _ms;
    }

    private void OnFrame() {
        var dt = Stage.GameTime.ElapsedMs;
        _ms += dt;
        var elapsed = _ms - _actStart;

        switch (_act) {
            case Act.EnterWalk:
            case Act.Wander:
                if (Walk(dt)) {
                    if (_act == Act.EnterWalk) {
                        Begin(Act.Look);
                    } else {
                        Begin(Act.Rest);
                    }
                }
                break;

            case Act.Look:              // turn to the reader, hold a beat, then speak
                _lookingAtYou = true;
                if (elapsed >= LookMs) {
                    Say(NextLine());
                    Begin(Act.Talk);
                }
                break;

            case Act.Talk:
                _lookingAtYou = true;
                if (elapsed >= TalkMs) {
                    HideBubble();
                    Begin(Act.Rest);
                }
                break;

            case Act.Cast:
                _lookingAtYou = false;
                if (elapsed >= CastMs) {
                    Begin(Act.Rest);
                }
                break;

            case Act.Rest:
                if (elapsed >= BetweenMs) {
                    PickNextAct();
                }
                break;
        }

        Render(elapsed);
    }

    // What to do after a pause: mostly talk, sometimes wander, sometimes a flick of the staff.
    private void PickNextAct() {
        var roll = _rng.Next(10);
        if (roll < 5) {
            Begin(Act.Look);
        } else if (roll < 8) {
            var margin = SpriteSize / 2f + 6;
            _targetX = _left + margin + (float)_rng.NextDouble() * (_right - _left - margin * 2);
            _facing = _targetX >= _x ? 1 : -1;
            _lookingAtYou = false;
            Begin(Act.Wander);
        } else {
            _facing = _rng.Next(2) == 0 ? 1 : -1;
            Begin(Act.Cast);
        }
    }

    // Moves toward _targetX; true when there.
    private bool Walk(double dt) {
        var step = (float)(WalkSpeed * dt);
        if (Math.Abs(_targetX - _x) <= step) {
            _x = _targetX;
            return true;
        }

        _x += MathF.Sign(_targetX - _x) * step;
        return false;
    }

    private string NextLine() {
        var lines = _mood == Mood.Register ? RegisterLines : SignInLines;
        int pick;
        do {
            pick = _rng.Next(lines.Length);
        } while (lines.Length > 1 && pick == _lastLine);
        _lastLine = pick;
        return lines[pick];
    }

    // The same look as the in-game SpeechBubble (Ui/Chat/SpeechBubble.cs), which needs a world entity to follow - this one follows the actor.
    private void Say(string text) {
        HideBubble();
        var txt = new SimpleText(new TextConfig { Text = text, FontSize = BubbleFont, X = BubbleCut, Y = BubbleCut, Color = BubbleText, MaxWidth = BubbleMaxWidth });
        var rect = new CutEdgeRect(new CutEdgeConfig { Width = BubbleCut * 2 + txt.Width, Height = BubbleCut * 2 + txt.Height, CutX = BubbleCut, CutY = BubbleCut, Color = BubbleBack });
        var edge = new CutEdgeRect(new CutEdgeConfig { Width = (BubbleCut + BubbleOutline) * 2 + txt.Width, Height = (BubbleCut + BubbleOutline) * 2 + txt.Height, CutX = BubbleCut + BubbleOutline / 2, CutY = BubbleCut + BubbleOutline / 2, Color = BubbleEdge }) { X = -BubbleOutline, Y = -BubbleOutline };
        _bubble = new Container();
        _bubble.AddChild(edge);
        _bubble.AddChild(rect);
        _bubble.AddChild(txt);
        _bubble.SetAnchor(UiAnchor.MiddleBottom);
        AddChild(_bubble);
        PlaceBubble();
    }

    private void HideBubble() {
        if (_bubble == null) {
            return;
        }

        RemoveChild(_bubble);
        _bubble = null;
    }

    // Above the head, kept inside the page sideways.
    private void PlaceBubble() {
        if (_bubble == null) {
            return;
        }

        var half = _bubble.Width / 2f;
        var bx = Math.Clamp(_x, _left + half + 4, _right - half - 4);
        _bubble.X = (int)bx;
        _bubble.Y = (int)(_y - SpriteSize + FootInset - BubbleGap);
    }

    private void Render(double elapsed) {
        var frames = Main.Atlas.GetAnimationAtlasData("players", WizardArt);
        var walking = _act is Act.EnterWalk or Act.Wander;
        var casting = _act == Act.Cast;
        var down = _lookingAtYou && !walking && !casting;
        var frame = walking ? 1 + (int)(_ms / WalkFrameMs) % 2 : casting ? IdleAttackFrame : 0;
        var set = down ? frames.FaceDown : frames.FaceRight;
        if (frame >= set.Length) {
            frame = 0;
        }

        if (frame != _shownFrame || down != _shownDown) {
            _shownFrame = frame;
            _shownDown = down;
            _body.ChangeTexture(TextureHelper.Create(set[frame], TextureType.GameAtlas));
        }

        _body.FlipX = !down && _facing < 0;

        // A little bob while walking, a breath at rest, a recoil while casting.
        var lift = walking ? -MathF.Abs(MathF.Sin((float)_ms * 0.014f)) * 3f : MathF.Sin((float)_ms * 0.004f) * 1.2f;
        var recoil = 0f;
        if (casting) {
            var p = Math.Clamp((float)elapsed / CastMs, 0f, 1f);
            recoil = -MathF.Sin(p * MathF.PI) * 7f * _facing;
        }

        // The double-width attack frame is drawn twice as wide so its pixels stay square.
        var wide = frame == IdleAttackFrame;
        _body.Resize(wide ? SpriteSize * 2 : SpriteSize, SpriteSize);
        _body.X = (int)(_x + recoil);
        _body.Y = (int)(_y - SpriteSize / 2f + FootInset + lift);

        PlaceBubble();
    }
}
