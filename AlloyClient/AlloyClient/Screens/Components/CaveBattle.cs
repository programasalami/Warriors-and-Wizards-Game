using System;
using System.Collections.Generic;
using AlloyClient.Utils;
using Alloy.Common.Structs;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.UiLib.Data;
using Alloy.UiLib.Extra;

namespace AlloyClient.Screens.Components;

// The main menu's living background: on the open floor either side of the menu scroll, small skirmishes play out forever.
// Wizards and Warriors (the same "players" sprites as the character cards) walk in from the cave edge and fight waves of
// monsters that crack into being out of lightning strikes. Wizards kite and throw bolts, Warriors close in and swing, the
// monsters lunge or spit fire back, everything flashes when hit, throws off sparks, floats damage numbers and (for the
// monsters) bursts apart when it dies. When a wave falls the next one is summoned; when the last is down the heroes cheer,
// walk off and a new party wanders in a few seconds later. The left and right sides run independently, so there is nearly
// always something going on somewhere.
//
// Everything here is drawn from art the game already has: the players sheet (Wizard/Warrior), the 16x16 monster sheet
// (the "caveMonsters" sheet), the effect/projectile sprites in the "icons" sheet, and the soft glow sprites of the cave map (CaveBackdrop). It
// is all in the cave's 1280x720 design space (the backdrop scales it to the window). Tune with the constants below.
public sealed class CaveBattle : Container {

    // ---- Sizes (design px) --------------------------------------------------------------------------------------------
    // The 32x32 hero art only fills about half its cell (and the 16x16 monster art fills all of its), so the heroes are drawn
    // from a much bigger cell to end up about the same height as the monsters.
    private const int HeroCell = 140;
    private const float HeroFootInset = HeroCell * 9f / 32f;   // empty rows under the feet of the hero art
    private const int EnemySize = 68;
    private const float MaxStepMs = 50f;             // longest simulation step (a hitch must not teleport anybody)

    // ---- Encounter timing (ms) -----------------------------------------------------------------------------------------
    private const float FirstLeftMs = 1800f;
    private const float FirstRightMs = 4800f;
    private const float RestMinMs = 2500f;
    private const float RestMaxMs = 5500f;
    private const float WaveGapMs = 1100f;           // quiet time between the last monster falling and the next wave
    private const float VictoryMs = 1900f;

    // ---- Combat ---------------------------------------------------------------------------------------------------------
    private const float WizardSpeed = 58f;
    private const float WarriorSpeed = 68f;
    private const float WizardRange = 175f;
    private const float WarriorReach = 50f;
    private const float BoltSpeed = 340f;
    private const float OrbSpeed = 175f;
    private const float FlashMs = 110f;

    // ---- Effect sprite indices in the "icons" sheet (8x8 art; see the sheet; moved there 2026-09-20) -------------------------------------------------------
    private const int FxBoltPurple = 21;          // horizontal purple bolt (bright head on the left)
    private const int FxBladeSwing = 16;          // the Blade projectile's picture, used as the Warrior's swing
    private const int FxOrbFire = 11;
    private const int FxOrbBlue = 12;
    private const int FxOrbPurple = 13;
    private const int FxImpactX = 20;             // orange X burst
    private const int FxImpactWhite = 19;
    private const int FxSplatRed = 8;

    // Glow kinds of the cave's glow strip: teal, green, amber, purple, white, fog.
    private const int GlowTeal = 0, GlowGreen = 1, GlowAmber = 2, GlowPurple = 3, GlowWhite = 4, GlowFog = 5;

    // The dust/spark burst sprite that goes with each glow colour (for hits and deaths).
    private static readonly int[] SparkByGlow = [9, 14, 15, 13, 10];

    private readonly record struct Arena(float MinX, float MaxX, float MinY, float MaxY, int EdgeDir) {
        public float MidY => (MinY + MaxY) / 2f;
    }

    // Open floor either side of the menu scroll (which covers x 480-800 and most of the height).
    private static readonly Arena LeftArena = new(105f, 445f, 335f, 640f, -1);
    private static readonly Arena RightArena = new(835f, 1175f, 335f, 640f, 1);

    // ---- The monster roster: cell in caveMonsters, hit points, speed, glow colour, ranged (orb sprite) or melee (-1) ----
    private readonly record struct Species(int Cell, float Hp, float Speed, int Glow, int Orb);

    private static readonly Species[] Monsters = [
        new(0, 110, 32, GlowWhite, -1),    // grey stone golem
        new(1, 120, 30, GlowAmber, -1),    // brown ogre
        new(2, 110, 30, GlowTeal, -1),     // ice golem
        new(3, 100, 36, GlowGreen, -1),    // green ogre
        new(4, 130, 26, GlowGreen, -1),    // mossy golem
        new(5, 90, 44, GlowPurple, -1),    // dark blue demon
        new(6, 85, 22, GlowGreen, FxOrbFire),  // olive beholder
        new(7, 70, 58, GlowGreen, -1),     // green spider
        new(8, 75, 48, GlowAmber, -1),     // winged sprite
        new(9, 80, 34, GlowPurple, FxOrbPurple),   // purple naga
        new(10, 90, 40, GlowAmber, -1),     // tan ghoul
        new(11, 120, 28, GlowAmber, -1),    // sand golem
        new(12, 70, 58, GlowTeal, -1),      // ice spider
        new(13, 100, 36, GlowTeal, -1),     // ice knight
        new(14, 110, 30, GlowTeal, -1),     // frost giant
        new(15, 60, 62, GlowAmber, -1),     // hawk
        new(16, 75, 30, GlowTeal, FxOrbBlue),       // ghost
        new(17, 70, 34, GlowPurple, FxOrbPurple),   // purple imp
        new(18, 80, 34, GlowAmber, FxOrbFire),      // flaming skull
        new(19, 75, 34, GlowAmber, FxOrbFire),      // flame wraith
        new(20, 95, 30, GlowAmber, FxOrbFire),      // phoenix
    ];

    private enum State { Walking, Fighting, Cheering, Leaving, Spawning, Dying, Gone }

    private sealed class Actor {
        public bool IsHero;
        public bool IsWizard;
        public int Art;                 // hero: index in the players sheet; enemy: cell in caveMonsters
        public Species Species;
        public Encounter Enc;

        public Container Root;
        public ObjectRect Body;
        public ObjectRect Shadow;
        public ColorRect BarBack;
        public ColorRect BarFill;

        public float X, Y;
        public float Hp, MaxHp, Speed;
        public State State;
        public double StateStart;

        public Actor Target;
        public double NextRetarget;
        public double NextAttack;
        public double AttackStart = -1;
        public bool AttackDone;
        public float Facing = 1f;
        public bool Moving;
        public float DestX, DestY;
        public float StrafeDir = 1f;
        public double StrafeUntil;
        public float Phase;

        public double HurtUntil;
        public bool HurtRed;
        public float KnockX, KnockY;
        public bool BarShown;

        // Body currently showing (so the picture is only swapped when it changes).
        public int ShownFrame = -1;
        public int ShownFacingKind = -1;
    }

    private sealed class Encounter {
        public Arena Zone;
        public readonly List<Actor> Heroes = [];
        public readonly List<Actor> Enemies = [];
        public int Phase;               // 0 resting, 1 heroes walking in, 2 fighting, 3 victory, 4 leaving
        public double PhaseStart;
        public double NextStart;
        public int WavesLeft;
        public double LastWaveClearedAt;
    }

    private sealed class Fx {
        public ObjectRect Rect;
        public double Start;
        public float Life;
        public float X, Y, Vx, Vy, Ay;
        public float BaseSize, Size0, Size1;
        public float A0, A1;
        public float Rot, Spin;
    }

    private sealed class Projectile {
        public ObjectRect Rect;
        public Encounter Enc;
        public bool FromHero;
        public float X, Y, Vx, Vy;
        public float Damage;
        public double Dies;
        public int SparkGlow;
        public int ImpactSprite;
    }

    private sealed class Strike {
        public readonly List<ColorRect> Rects = [];
        public double Start;
    }

    private sealed class Pop {
        public SimpleText Text;
        public double Start;
        public float X, Y;
    }

    private readonly Random _rng = new();
    private readonly Container _actorLayer = new();
    private readonly Container _fxLayer = new();

    private readonly List<Actor> _actors = [];
    private readonly List<Fx> _fx = [];
    private readonly List<Projectile> _projectiles = [];
    private readonly List<Strike> _strikes = [];
    private readonly List<Pop> _pops = [];
    private readonly Stack<ObjectRect> _rectPool = new();
    private readonly List<Actor> _sortScratch = [];

    private readonly Encounter[] _encounters;
    private double _lastMs = -1;
    private double _startMs = -1;

    public CaveBattle() {
        AddChild(_actorLayer);
        AddChild(_fxLayer);

        _encounters = [
            new Encounter { Zone = LeftArena },
            new Encounter { Zone = RightArena },
        ];

        AddEventListener(Event.EnterFrame, OnFrame);
    }

    // ==================================================================================================================
    // Frame
    // ==================================================================================================================

    private void OnFrame() {
        var ms = (double)Stage.GameTime.TotalMs;
        if (_startMs < 0) {
            _startMs = ms;
            _lastMs = ms;
            _encounters[0].NextStart = ms + FirstLeftMs;
            _encounters[1].NextStart = ms + FirstRightMs;
        }

        var dt = (float)Math.Min(ms - _lastMs, MaxStepMs);
        _lastMs = ms;

        foreach (var enc in _encounters) {
            UpdateEncounter(enc, ms);
        }

        for (var i = _actors.Count - 1; i >= 0; i--) {
            var a = _actors[i];
            UpdateActor(a, ms, dt);
            RenderActor(a, ms);
            if (a.State == State.Gone) {
                _actorLayer.RemoveChild(a.Root);
                _actors.RemoveAt(i);
            }
        }

        UpdateProjectiles(ms, dt);
        UpdateFx(ms, dt);
        UpdateStrikes(ms);
        UpdatePops(ms);
        SortActors();
    }

    // Draw order by ground line, so whoever is lower on the screen is in front.
    private void SortActors() {
        _sortScratch.Clear();
        _sortScratch.AddRange(_actors);
        _sortScratch.Sort((p, q) => p.Y.CompareTo(q.Y));
        for (var i = 0; i < _sortScratch.Count; i++) {
            if (_actorLayer.GetChildAt(i) != _sortScratch[i].Root) {
                _actorLayer.SetChildIndex(_sortScratch[i].Root, i);
            }
        }
    }

    // ==================================================================================================================
    // Encounters
    // ==================================================================================================================

    private void UpdateEncounter(Encounter enc, double ms) {
        switch (enc.Phase) {
            case 0:
                if (ms >= enc.NextStart) {
                    StartEncounter(enc, ms);
                }
                break;

            case 1: {
                var arrived = true;
                foreach (var hero in enc.Heroes) {
                    if (hero.State == State.Walking) {
                        arrived = false;
                    }
                }
                if (arrived) {
                    enc.Phase = 2;
                    enc.PhaseStart = ms;
                    enc.LastWaveClearedAt = ms - WaveGapMs + 500;   // first wave comes almost at once
                }
                break;
            }

            case 2:
                if (enc.Enemies.Count == 0) {
                    if (ms - enc.LastWaveClearedAt >= WaveGapMs) {
                        if (enc.WavesLeft > 0) {
                            enc.WavesLeft--;
                            SpawnWave(enc, ms);
                        } else {
                            enc.Phase = 3;
                            enc.PhaseStart = ms;
                            foreach (var hero in enc.Heroes) {
                                hero.State = State.Cheering;
                                hero.StateStart = ms;
                                hero.Moving = false;
                                hero.Target = null;
                            }
                        }
                    }
                } else {
                    enc.LastWaveClearedAt = ms;
                }
                break;

            case 3:
                if (ms - enc.PhaseStart >= VictoryMs) {
                    enc.Phase = 4;
                    foreach (var hero in enc.Heroes) {
                        hero.State = State.Leaving;
                        hero.StateStart = ms;
                    }
                }
                break;

            case 4: {
                var anyLeft = false;
                foreach (var hero in enc.Heroes) {
                    if (hero.State != State.Gone) {
                        anyLeft = true;
                    }
                }
                if (!anyLeft) {
                    enc.Heroes.Clear();
                    enc.Phase = 0;
                    enc.NextStart = ms + RestMinMs + _rng.NextDouble() * (RestMaxMs - RestMinMs);
                }
                break;
            }
        }
    }

    private void StartEncounter(Encounter enc, double ms) {
        enc.Phase = 1;
        enc.PhaseStart = ms;
        enc.Heroes.Clear();
        enc.Enemies.Clear();
        enc.WavesLeft = 2 + _rng.Next(2);

        // Half the time a Wizard and a Warrior together, otherwise one of them alone.
        var roll = _rng.NextDouble();
        var classes = roll < 0.55 ? new[] { 0, 1 } : roll < 0.78 ? new[] { 0 } : new[] { 1 };

        var zone = enc.Zone;
        var edgeX = zone.EdgeDir < 0 ? -HeroCell / 2f - 10f : 1280 + HeroCell / 2f + 10f;
        for (var i = 0; i < classes.Length; i++) {
            var hero = MakeHero(enc, classes[i]);
            hero.X = edgeX - zone.EdgeDir * i * 70f;
            hero.Y = zone.MidY + (i == 0 ? -50f : 55f) + (float)(_rng.NextDouble() * 30 - 15);
            hero.DestX = zone.EdgeDir < 0 ? zone.MinX + 40f + i * 75f : zone.MaxX - 40f - i * 75f;
            hero.DestY = hero.Y;
            hero.Facing = -zone.EdgeDir;
            hero.State = State.Walking;
            hero.StateStart = ms;
            enc.Heroes.Add(hero);
        }
    }

    private void SpawnWave(Encounter enc, double ms) {
        var count = enc.Heroes.Count == 1 ? 2 + _rng.Next(2) : 3 + _rng.Next(2);
        var zone = enc.Zone;
        for (var i = 0; i < count; i++) {
            var species = Monsters[_rng.Next(Monsters.Length)];
            var enemy = MakeEnemy(enc, species);

            // Somewhere on the far side of the arena from where the heroes came in, not on top of anybody.
            for (var attempt = 0; attempt < 12; attempt++) {
                var far = zone.EdgeDir < 0 ? zone.MinX + 190f : zone.MinX;
                var farMax = zone.EdgeDir < 0 ? zone.MaxX : zone.MaxX - 190f;
                enemy.X = far + (float)_rng.NextDouble() * (farMax - far);
                enemy.Y = zone.MinY + 20f + (float)_rng.NextDouble() * (zone.MaxY - zone.MinY - 20f);
                if (!TooClose(enc, enemy, 80f)) {
                    break;
                }
            }

            enemy.State = State.Spawning;
            enemy.StateStart = ms + i * 220.0 + _rng.NextDouble() * 120.0;   // staggered strikes
            enemy.Body.Alpha = 0f;
            enemy.Shadow.Alpha = 0f;
            enc.Enemies.Add(enemy);
        }
    }

    private static bool TooClose(Encounter enc, Actor a, float distance) {
        foreach (var other in enc.Enemies) {
            if (Dist(a.X, a.Y, other.X, other.Y) < distance) {
                return true;
            }
        }
        foreach (var hero in enc.Heroes) {
            if (Dist(a.X, a.Y, hero.X, hero.Y) < distance + 60f) {
                return true;
            }
        }
        return false;
    }

    // ==================================================================================================================
    // Building actors
    // ==================================================================================================================

    private Actor MakeHero(Encounter enc, int cls) {
        var actor = new Actor {
            IsHero = true,
            IsWizard = cls == 0,
            Art = cls,
            Enc = enc,
            Hp = 1e9f,
            MaxHp = 1e9f,
            Speed = cls == 0 ? WizardSpeed : WarriorSpeed,
            Phase = (float)(_rng.NextDouble() * 6.28)
        };
        actor.Root = new Container();
        actor.Shadow = MakeShadow(64, 22);
        actor.Root.AddChild(actor.Shadow);
        actor.Body = new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.Create(Main.Atlas.GetAnimationAtlasData("players", cls).FaceRight[0], TextureType.GameAtlas),
            Width = HeroCell,
            Height = HeroCell,
            Anchor = UiAnchor.Middle,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        actor.Root.AddChild(actor.Body);
        actor.NextAttack = _lastMs + 400 + _rng.NextDouble() * 600;
        _actorLayer.AddChild(actor.Root);
        _actors.Add(actor);
        return actor;
    }

    private Actor MakeEnemy(Encounter enc, Species species) {
        var actor = new Actor {
            IsHero = false,
            Art = species.Cell,
            Species = species,
            Enc = enc,
            Hp = species.Hp,
            MaxHp = species.Hp,
            Speed = species.Speed * (0.9f + (float)_rng.NextDouble() * 0.25f),
            Phase = (float)(_rng.NextDouble() * 6.28)
        };
        actor.Root = new Container();
        actor.Shadow = MakeShadow(EnemySize - 8, 20);
        actor.Root.AddChild(actor.Shadow);
        actor.Body = new ObjectRect(new ObjectRectConfig {
            Texture = TextureHelper.FromGameAtlas("caveMonsters", species.Cell),
            Width = EnemySize,
            Height = EnemySize,
            Anchor = UiAnchor.Middle,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        actor.Root.AddChild(actor.Body);

        // A slim hit-point bar over its head, shown once it has taken damage.
        actor.BarBack = new ColorRect(new ColorRectConfig { Width = 36, Height = 5, Color = 0x120A05, Anchor = UiAnchor.Middle, Alpha = 0f });
        actor.BarFill = new ColorRect(new ColorRectConfig { Width = 34, Height = 3, Color = 0xB8324A, Alpha = 0f });
        actor.Root.AddChild(actor.BarBack);
        actor.Root.AddChild(actor.BarFill);

        actor.NextAttack = _lastMs + 900 + _rng.NextDouble() * 900;
        _actorLayer.AddChild(actor.Root);
        _actors.Add(actor);
        return actor;
    }

    // A soft dark blob under the feet: the cave map's fog glow tinted black.
    private static ObjectRect MakeShadow(int width, int height) {
        var rect = new ObjectRect(new ObjectRectConfig {
            Texture = CaveBackdrop.GlowTexture(GlowFog),
            Width = width,
            Height = height,
            Anchor = UiAnchor.Middle,
            Alpha = 0.55f,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        rect.ColorTransformation = new ColorTransform(0f, 0f, 0f, 1f);
        return rect;
    }

    // ==================================================================================================================
    // Actor behaviour
    // ==================================================================================================================

    private void UpdateActor(Actor a, double ms, float dt) {
        // Knockback settles quickly.
        a.KnockX *= MathF.Pow(0.002f, dt / 1000f);
        a.KnockY *= MathF.Pow(0.002f, dt / 1000f);

        if (a.IsHero) {
            UpdateHero(a, ms, dt);
        } else {
            UpdateEnemy(a, ms, dt);
        }
    }

    private void UpdateHero(Actor a, double ms, float dt) {
        var zone = a.Enc.Zone;
        a.Moving = false;

        switch (a.State) {
            case State.Walking:
                if (StepToward(a, a.DestX, a.DestY, a.Speed, dt, 3f)) {
                    a.State = State.Fighting;
                    a.StateStart = ms;
                }
                return;

            case State.Leaving: {
                var exitX = zone.EdgeDir < 0 ? -HeroCell / 2f - 20f : 1280 + HeroCell / 2f + 20f;
                StepToward(a, exitX, a.Y, a.Speed * 1.15f, dt, 0f);
                if (zone.EdgeDir < 0 ? a.X <= exitX + 2f : a.X >= exitX - 2f) {
                    a.State = State.Gone;
                }
                return;
            }

            case State.Cheering:
                return;

            case State.Fighting:
                break;

            default:
                return;
        }

        if (a.Target == null || a.Target.State != State.Fighting || ms >= a.NextRetarget) {
            a.Target = Nearest(a, a.Enc.Enemies);
            a.NextRetarget = ms + 500;
        }

        var target = a.Target;
        var attacking = a.AttackStart >= 0;

        if (target != null) {
            var dx = target.X - a.X;
            var dy = target.Y - a.Y;
            var dist = MathF.Sqrt(dx * dx + dy * dy);
            if (MathF.Abs(dx) > 6f && !attacking) {
                a.Facing = dx > 0 ? 1f : -1f;
            }

            if (!attacking) {
                if (a.IsWizard) {
                    // Keep a distance: back off when crowded, close in when far, drift sideways in between.
                    if (dist < WizardRange - 55f) {
                        StepAway(a, target, a.Speed, dt);
                    } else if (dist > WizardRange + 30f) {
                        StepToward(a, target.X, target.Y, a.Speed, dt, WizardRange);
                    } else {
                        Strafe(a, ms, dt, a.Speed * 0.45f);
                    }
                } else {
                    StepToward(a, target.X, target.Y, a.Speed, dt, WarriorReach - 8f);
                }
                KeepInArena(a);
            }

            var inRange = a.IsWizard ? dist < WizardRange + 90f : dist <= WarriorReach + 8f;
            if (!attacking && inRange && ms >= a.NextAttack) {
                a.AttackStart = ms;
                a.AttackDone = false;
                a.NextAttack = ms + (a.IsWizard ? 1050 : 800) * (0.85 + _rng.NextDouble() * 0.35);
            }
        }

        if (a.AttackStart >= 0) {
            var t = (float)(ms - a.AttackStart);
            if (a.IsWizard) {
                if (!a.AttackDone && t >= 170f && target != null) {
                    a.AttackDone = true;
                    FireHeroBolt(a, target);
                }
                if (t >= 340f) {
                    a.AttackStart = -1;
                }
            } else {
                if (!a.AttackDone && t >= 120f) {
                    a.AttackDone = true;
                    if (target != null && target.State == State.Fighting && Dist(a.X, a.Y, target.X, target.Y) <= WarriorReach + 26f) {
                        HeroSwing(a, target, ms);
                    } else {
                        HeroSwing(a, null, ms);
                    }
                }
                if (t >= 330f) {
                    a.AttackStart = -1;
                }
            }
        }
    }

    private void UpdateEnemy(Actor a, double ms, float dt) {
        a.Moving = false;

        switch (a.State) {
            case State.Spawning:
                UpdateSpawn(a, ms);
                return;

            case State.Dying:
                if (ms - a.StateStart >= 460) {
                    a.State = State.Gone;
                }
                return;

            case State.Fighting:
                break;

            default:
                return;
        }

        if (a.Target == null || a.Target.State != State.Fighting || ms >= a.NextRetarget) {
            a.Target = Nearest(a, a.Enc.Heroes, State.Fighting);
            a.NextRetarget = ms + 600;
        }

        var target = a.Target;
        var attacking = a.AttackStart >= 0;
        var ranged = a.Species.Orb >= 0;

        if (target != null) {
            var dx = target.X - a.X;
            var dy = target.Y - a.Y;
            var dist = MathF.Sqrt(dx * dx + dy * dy);
            if (MathF.Abs(dx) > 6f && !attacking) {
                a.Facing = dx > 0 ? 1f : -1f;
            }

            if (!attacking) {
                if (ranged) {
                    if (dist < 150f) {
                        StepAway(a, target, a.Speed, dt);
                    } else if (dist > 240f) {
                        StepToward(a, target.X, target.Y, a.Speed, dt, 230f);
                    } else {
                        Strafe(a, ms, dt, a.Speed * 0.5f);
                    }
                } else {
                    StepToward(a, target.X, target.Y, a.Speed, dt, 40f);
                }
                Separate(a);
                KeepInArena(a);
            }

            var inRange = ranged ? dist < 300f : dist <= 52f;
            if (!attacking && inRange && ms >= a.NextAttack) {
                a.AttackStart = ms;
                a.AttackDone = false;
                a.NextAttack = ms + (ranged ? 1900 : 1250) * (0.8 + _rng.NextDouble() * 0.5);
            }
        }

        if (a.AttackStart >= 0) {
            var t = (float)(ms - a.AttackStart);
            var windup = ranged ? 260f : 230f;
            if (!a.AttackDone && t >= windup) {
                a.AttackDone = true;
                if (target != null && target.State == State.Fighting) {
                    if (ranged) {
                        FireEnemyOrb(a, target);
                    } else if (Dist(a.X, a.Y, target.X, target.Y) <= 70f) {
                        Damage(target, 7f + (float)_rng.NextDouble() * 9f, a.Facing, false, ms);
                    }
                }
            }
            if (t >= 520f) {
                a.AttackStart = -1;
            }
        }
    }

    // The lightning strike, then the monster appears in a white-hot flash that cools back to normal.
    private void UpdateSpawn(Actor a, double ms) {
        var t = (float)(ms - a.StateStart);
        if (t < 0f) {
            return;
        }

        if (a.ShownFacingKind != 99) {
            a.ShownFacingKind = 99;    // strike launched
            LaunchStrike(a.X, a.Y - 18f, ms);
            AddGlow(GlowWhite, a.X, a.Y - 34f, 60f, 230f, 340f);
            AddGlow(a.Species.Glow, a.X, a.Y - 30f, 40f, 170f, 520f);
        }

        if (t >= 150f) {
            var k = Math.Clamp((t - 150f) / 300f, 0f, 1f);
            a.Body.Alpha = 1f;
            a.Shadow.Alpha = 0.55f * k;
            a.HurtUntil = a.StateStart + 400;
            a.HurtRed = false;
        }

        if (t >= 480f) {
            a.State = State.Fighting;
            a.StateStart = ms;
            a.NextAttack = ms + 500 + _rng.NextDouble() * 800;
        }
    }

    private static bool StepToward(Actor a, float tx, float ty, float speed, float dt, float stopDist) {
        var dx = tx - a.X;
        var dy = ty - a.Y;
        var d = MathF.Sqrt(dx * dx + dy * dy);
        if (d <= stopDist || d < 0.001f) {
            return true;
        }

        var step = MathF.Min(speed * dt / 1000f, d - stopDist);
        a.X += dx / d * step;
        a.Y += dy / d * step;
        a.Moving = true;
        if (MathF.Abs(dx) > 3f) {
            a.Facing = dx > 0 ? 1f : -1f;
        }
        return false;
    }

    private static void StepAway(Actor a, Actor from, float speed, float dt) {
        var dx = a.X - from.X;
        var dy = a.Y - from.Y;
        var d = MathF.Max(MathF.Sqrt(dx * dx + dy * dy), 0.001f);
        var step = speed * 0.9f * dt / 1000f;
        a.X += dx / d * step;
        a.Y += dy / d * step;
        a.Moving = true;
    }

    private void Strafe(Actor a, double ms, float dt, float speed) {
        if (ms >= a.StrafeUntil) {
            a.StrafeDir = _rng.NextDouble() < 0.5 ? -1f : 1f;
            a.StrafeUntil = ms + 700 + _rng.NextDouble() * 900;
        }
        a.Y += a.StrafeDir * speed * dt / 1000f;
        a.Moving = true;
    }

    private static void Separate(Actor a) {
        foreach (var other in a.Enc.Enemies) {
            if (other == a || other.State != State.Fighting) {
                continue;
            }
            var dx = a.X - other.X;
            var dy = a.Y - other.Y;
            var d = MathF.Sqrt(dx * dx + dy * dy);
            if (d < 46f && d > 0.01f) {
                var push = (46f - d) * 0.06f;
                a.X += dx / d * push;
                a.Y += dy / d * push;
            }
        }
    }

    private static void KeepInArena(Actor a) {
        var z = a.Enc.Zone;
        a.X = Math.Clamp(a.X, z.MinX, z.MaxX);
        a.Y = Math.Clamp(a.Y, z.MinY, z.MaxY);
    }

    private static Actor Nearest(Actor from, List<Actor> list, State? require = null) {
        Actor best = null;
        var bestD = float.MaxValue;
        foreach (var other in list) {
            if (other.State == State.Gone || other.State == State.Dying || other.State == State.Spawning) {
                continue;
            }
            if (require.HasValue && other.State != require.Value) {
                continue;
            }
            var d = Dist(from.X, from.Y, other.X, other.Y);
            if (d < bestD) {
                bestD = d;
                best = other;
            }
        }
        return best;
    }

    private static float Dist(float x1, float y1, float x2, float y2) {
        var dx = x2 - x1;
        var dy = y2 - y1;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    // ==================================================================================================================
    // Attacks, damage and death
    // ==================================================================================================================

    // The Warrior's swing: a blade sweeps across in front of him and, if there is anybody in reach, hits them.
    private void HeroSwing(Actor hero, Actor target, double ms) {
        var dir = hero.Facing;
        var fx = AddSprite(LofiObj(FxBladeSwing), hero.X + dir * 34f, hero.Y - 40f, 44f, 50f, 190f, 1f, 0f);
        fx.Rot = dir > 0 ? -1.2f : MathF.PI + 1.2f;
        fx.Spin = dir * 13.5f;   // radians per second

        if (target == null) {
            return;
        }

        var dmg = 18f + (float)_rng.NextDouble() * 14f;
        Damage(target, dmg, dir, true, ms);
    }

    private void FireHeroBolt(Actor hero, Actor target) {
        var sx = hero.X + hero.Facing * 26f;
        var sy = hero.Y - 42f;
        var tx = target.X;
        var ty = target.Y - 30f;
        LaunchProjectile(hero.Enc, true, FxBoltPurple, 30, sx, sy, tx, ty, BoltSpeed, 16f + (float)_rng.NextDouble() * 10f, GlowPurple, FxOrbPurple, MathF.PI);
        AddGlow(GlowPurple, sx, sy, 20f, 60f, 240f);
    }

    private void FireEnemyOrb(Actor enemy, Actor target) {
        var sx = enemy.X + enemy.Facing * 20f;
        var sy = enemy.Y - 38f;
        var orb = enemy.Species.Orb;
        LaunchProjectile(enemy.Enc, false, orb, 30, sx, sy, target.X, target.Y - 30f, OrbSpeed, 8f + (float)_rng.NextDouble() * 8f, enemy.Species.Glow, FxImpactX, 0f);
        AddGlow(enemy.Species.Glow, sx, sy, 20f, 56f, 220f);
    }

    private void LaunchProjectile(Encounter enc, bool fromHero, int sprite, int size, float sx, float sy, float tx, float ty, float speed, float damage, int sparkGlow, int impact, float headAngle) {
        var angle = MathF.Atan2(ty - sy, tx - sx);
        var rect = Rent(LofiObj(sprite), size, size);
        rect.X = (int)sx;
        rect.Y = (int)sy;
        rect.Rotation = angle - headAngle;
        _projectiles.Add(new Projectile {
            Rect = rect,
            Enc = enc,
            FromHero = fromHero,
            X = sx,
            Y = sy,
            Vx = MathF.Cos(angle) * speed,
            Vy = MathF.Sin(angle) * speed,
            Damage = damage,
            Dies = _lastMs + 2200,
            SparkGlow = sparkGlow,
            ImpactSprite = impact
        });
    }

    private void UpdateProjectiles(double ms, float dt) {
        for (var i = _projectiles.Count - 1; i >= 0; i--) {
            var p = _projectiles[i];
            p.X += p.Vx * dt / 1000f;
            p.Y += p.Vy * dt / 1000f;
            p.Rect.X = (int)p.X;
            p.Rect.Y = (int)p.Y;

            var victims = p.FromHero ? p.Enc.Enemies : p.Enc.Heroes;
            Actor hit = null;
            foreach (var v in victims) {
                if (v.State != State.Fighting) {
                    continue;
                }
                if (Dist(p.X, p.Y, v.X, v.Y - 30f) < 30f) {
                    hit = v;
                    break;
                }
            }

            if (hit != null) {
                Damage(hit, p.Damage, MathF.Sign(p.Vx), p.FromHero, ms);
                AddSprite(LofiObj(p.ImpactSprite), p.X, p.Y, 26f, 58f, 260f, 1f, 0f);
                AddGlow(p.SparkGlow, p.X, p.Y, 24f, 80f, 260f);
                Return(p.Rect);
                _projectiles.RemoveAt(i);
            } else if (ms >= p.Dies || p.X < -80f || p.X > 1360f || p.Y < -80f || p.Y > 800f) {
                Return(p.Rect);
                _projectiles.RemoveAt(i);
            }
        }
    }

    // One hit: flash, shove, a damage number and sparks - and if it was the last of its hit points, death.
    private void Damage(Actor target, float amount, float pushDir, bool byHero, double ms) {
        if (target.State == State.Dying || target.State == State.Gone) {
            return;
        }

        var dmg = MathF.Round(amount);
        target.Hp -= dmg;
        target.HurtUntil = ms + FlashMs;
        target.HurtRed = target.IsHero;
        target.KnockX += pushDir * (target.IsHero ? 4f : 7f);

        AddPop(((int)dmg).ToString(), target.X + (float)(_rng.NextDouble() * 16 - 8), target.Y - 68f, byHero ? 0xFFE9A8u : 0xFF7A66u);

        var glow = target.IsHero ? GlowWhite : target.Species.Glow;
        AddSprite(LofiObj(target.IsHero ? FxSplatRed : FxImpactX), target.X - pushDir * 4f, target.Y - 34f, 30f, 62f, 230f, 1f, 0f);
        SparkBurst(target.X, target.Y - 34f, glow, target.IsHero ? 3 : 5);

        if (!target.IsHero) {
            target.BarShown = true;
            if (target.Hp <= 0f) {
                Kill(target, ms);
            }
        }
    }

    private void Kill(Actor enemy, double ms) {
        enemy.State = State.Dying;
        enemy.StateStart = ms;
        enemy.AttackStart = -1;
        enemy.Enc.Enemies.Remove(enemy);
        enemy.Enc.LastWaveClearedAt = ms;

        AddGlow(enemy.Species.Glow, enemy.X, enemy.Y - 34f, 50f, 190f, 480f);
        AddGlow(GlowWhite, enemy.X, enemy.Y - 34f, 30f, 120f, 260f);
        AddSprite(LofiObj(FxImpactWhite), enemy.X, enemy.Y - 34f, 40f, 110f, 380f, 1f, 0f);
        SparkBurst(enemy.X, enemy.Y - 34f, enemy.Species.Glow, 9);
    }

    // A handful of sparks that fly out, fall a little and fade.
    private void SparkBurst(float x, float y, int glow, int count) {
        for (var i = 0; i < count; i++) {
            var angle = (float)(_rng.NextDouble() * Math.Tau);
            var speed = 60f + (float)_rng.NextDouble() * 120f;
            var fx = AddSprite(LofiObj(SparkByGlow[glow]), x, y, 12f + (float)_rng.NextDouble() * 8f, 4f, 420f + (float)_rng.NextDouble() * 300f, 1f, 0f);
            fx.Vx = MathF.Cos(angle) * speed;
            fx.Vy = MathF.Sin(angle) * speed - 40f;
            fx.Ay = 220f;
        }
    }

    // ==================================================================================================================
    // Drawing actors
    // ==================================================================================================================

    private void RenderActor(Actor a, double ms) {
        a.Root.X = (int)MathF.Round(a.X + a.KnockX);
        a.Root.Y = (int)MathF.Round(a.Y + a.KnockY);

        if (a.State == State.Gone) {
            return;
        }

        // Hit flash: red for heroes, white-hot for monsters.
        if (ms < a.HurtUntil) {
            a.Body.ColorTransformation = a.HurtRed ? new ColorTransform(2.1f, 0.7f, 0.7f, 1f) : new ColorTransform(2.6f, 2.6f, 2.6f, 1f);
        } else {
            a.Body.ColorTransformation = ColorTransform.Default;
        }

        if (a.IsHero) {
            RenderHero(a, ms);
        } else {
            RenderEnemy(a, ms);
        }
    }

    private void RenderHero(Actor a, double ms) {
        var frames = Main.Atlas.GetAnimationAtlasData("players", a.Art);
        var walking = a.Moving && a.State != State.Cheering;
        var frame = walking ? 1 + (int)(ms / 200.0) % 2 : 0;
        if (frame != a.ShownFrame) {
            a.ShownFrame = frame;
            a.Body.ChangeTexture(TextureHelper.Create(frames.FaceRight[frame], TextureType.GameAtlas));
        }
        a.Body.FlipX = a.Facing < 0f;

        var baseY = -HeroCell / 2f + HeroFootInset;
        var lift = 0f;
        var lunge = 0f;
        var squash = 1f;

        if (a.State == State.Cheering) {
            lift = -MathF.Abs(MathF.Sin((float)(ms - a.StateStart) * 0.011f + a.Phase)) * 14f;
        } else if (walking) {
            lift = -MathF.Abs(MathF.Sin((float)ms * 0.014f + a.Phase)) * 3f;
        } else {
            lift = MathF.Sin((float)ms * 0.004f + a.Phase) * 1.2f;
        }

        if (a.AttackStart >= 0) {
            var p = Math.Clamp((float)(ms - a.AttackStart) / (a.IsWizard ? 340f : 330f), 0f, 1f);
            if (a.IsWizard) {
                lunge = -MathF.Sin(p * MathF.PI) * 7f;      // a little recoil as the bolt leaves
                squash = 1f + MathF.Sin(p * MathF.PI) * 0.06f;
            } else {
                lunge = MathF.Sin(p * MathF.PI) * 18f;      // a lunge into the swing
            }
        }

        a.Body.X = (int)MathF.Round(a.Facing * lunge);
        a.Body.Y = (int)MathF.Round(baseY + lift);
        a.Body.Scale = new OpenTK.Mathematics.Vector2(1f, squash);
    }

    private void RenderEnemy(Actor a, double ms) {
        var baseY = -EnemySize / 2f + 4f;
        var hop = 0f;
        var lunge = 0f;
        var rot = 0f;

        if (a.State == State.Dying) {
            var p = Math.Clamp((float)(ms - a.StateStart) / 460f, 0f, 1f);
            a.Body.Alpha = 1f - p * p;
            a.Shadow.Alpha = 0.55f * (1f - p);
            a.Body.Scale = new OpenTK.Mathematics.Vector2(1f + p * 0.25f, 1f - p * 0.3f);
            a.Body.Y = (int)MathF.Round(baseY - p * 10f);
            a.BarBack.Alpha = 0f;
            a.BarFill.Alpha = 0f;
            a.Body.FlipX = a.Facing < 0f;
            return;
        }

        if (a.State == State.Spawning) {
            a.Body.X = 0;
            a.Body.Y = (int)MathF.Round(baseY);
            return;
        }

        if (a.Moving) {
            var w = MathF.Sin((float)ms * 0.011f * (a.Speed / 36f) + a.Phase);
            hop = -MathF.Abs(w) * 5f;
            rot = w * 0.07f;
        } else {
            hop = MathF.Sin((float)ms * 0.005f + a.Phase) * 1.6f;
        }

        if (a.AttackStart >= 0) {
            var p = Math.Clamp((float)(ms - a.AttackStart) / 520f, 0f, 1f);
            // Rear back, then snap forward.
            lunge = p < 0.45f ? -MathF.Sin(p / 0.45f * MathF.PI * 0.5f) * 6f : MathF.Sin((p - 0.45f) / 0.55f * MathF.PI) * 22f;
        }

        a.Body.FlipX = a.Facing < 0f;
        a.Body.X = (int)MathF.Round(a.Facing * lunge);
        a.Body.Y = (int)MathF.Round(baseY + hop);
        a.Body.Rotation = rot;
        a.Body.Scale = new OpenTK.Mathematics.Vector2(1f, 1f);
        a.Body.Alpha = 1f;

        if (a.BarShown) {
            var frac = Math.Clamp(a.Hp / a.MaxHp, 0f, 1f);
            var barY = (int)MathF.Round(baseY - EnemySize / 2f + 2f);
            a.BarBack.X = 0;
            a.BarBack.Y = barY;
            a.BarBack.Alpha = 0.85f;
            a.BarFill.X = -17;
            a.BarFill.Y = barY - 1;
            a.BarFill.Alpha = 1f;
            a.BarFill.Resize(Math.Max(1, (int)(34f * frac)), 3);
        }
    }

    // ==================================================================================================================
    // Effects
    // ==================================================================================================================

    private static TextureInfo LofiObj(int index) => TextureHelper.FromGameAtlas("icons", index);

    private ObjectRect Rent(TextureInfo texture, int width, int height) {
        ObjectRect rect;
        if (_rectPool.Count > 0) {
            rect = _rectPool.Pop();
            rect.ChangeTexture(texture);
            rect.Resize(width, height);
        } else {
            rect = new ObjectRect(new ObjectRectConfig {
                Texture = texture,
                Width = width,
                Height = height,
                Anchor = UiAnchor.Middle,
                OutlineEnabled = false,
                GlowEnabled = false
            });
            _fxLayer.AddChild(rect);
        }

        rect.Visible = true;
        rect.Alpha = 1f;
        rect.Rotation = 0f;
        rect.FlipX = false;
        rect.Scale = new OpenTK.Mathematics.Vector2(1f, 1f);
        rect.ColorTransformation = ColorTransform.Default;
        return rect;
    }

    private void Return(ObjectRect rect) {
        rect.Visible = false;
        _rectPool.Push(rect);
    }

    // A sprite that grows/shrinks from size0 to size1 and fades from a0 to a1 over its life.
    private Fx AddSprite(TextureInfo texture, float x, float y, float size0, float size1, float lifeMs, float a0, float a1) {
        var rect = Rent(texture, (int)size0, (int)size0);
        var fx = new Fx {
            Rect = rect,
            Start = _lastMs,
            Life = lifeMs,
            X = x,
            Y = y,
            BaseSize = size0,
            Size0 = size0,
            Size1 = size1,
            A0 = a0,
            A1 = a1
        };
        rect.X = (int)x;
        rect.Y = (int)y;
        rect.Alpha = a0;
        _fx.Add(fx);
        return fx;
    }

    private void AddGlow(int glow, float x, float y, float size0, float size1, float lifeMs) {
        AddSprite(CaveBackdrop.GlowTexture(glow), x, y, size0, size1, lifeMs, 0.95f, 0f);
    }

    private void UpdateFx(double ms, float dt) {
        for (var i = _fx.Count - 1; i >= 0; i--) {
            var fx = _fx[i];
            var t = (float)(ms - fx.Start) / fx.Life;
            if (t >= 1f) {
                Return(fx.Rect);
                _fx.RemoveAt(i);
                continue;
            }

            fx.Vy += fx.Ay * dt / 1000f;
            fx.X += fx.Vx * dt / 1000f;
            fx.Y += fx.Vy * dt / 1000f;
            fx.Rot += fx.Spin * dt / 1000f;

            var size = fx.Size0 + (fx.Size1 - fx.Size0) * t;
            fx.Rect.X = (int)fx.X;
            fx.Rect.Y = (int)fx.Y;
            fx.Rect.Rotation = fx.Rot;
            fx.Rect.Scale = new OpenTK.Mathematics.Vector2(size / fx.BaseSize);
            fx.Rect.Alpha = fx.A0 + (fx.A1 - fx.A0) * t;
        }
    }

    // ---- Lightning: a jagged pixel bolt from the top of the cave down to the spot, flickering for a moment ----------------
    private const int StrikeLifeMs = 300;

    private void LaunchStrike(float x, float bottomY, double ms) {
        var strike = new Strike { Start = ms };

        var cx = x + (float)(_rng.NextDouble() * 60 - 30);
        var cy = -10f;
        while (cy < bottomY) {
            var len = Math.Min(24f + (float)_rng.NextDouble() * 30f, bottomY - cy);
            AddBoltPiece(strike, cx - 2f, cy, 4, (int)len);
            cy += len;

            // A sideways jog between the vertical pieces, drifting back toward the target column.
            var jog = (float)(_rng.NextDouble() * 22 - 11) + (x - cx) * 0.25f;
            if (cy < bottomY - 6f) {
                AddBoltPiece(strike, MathF.Min(cx, cx + jog) - 2f, cy - 2f, (int)MathF.Abs(jog) + 4, 4);
                cx += jog;
            }
        }

        _strikes.Add(strike);
    }

    private void AddBoltPiece(Strike strike, float x, float y, int w, int h) {
        // A wide pale-blue halo under a thin white core.
        var halo = new ColorRect(new ColorRectConfig { X = (int)x - 3, Y = (int)y - 3, Width = w + 6, Height = h + 6, Color = 0x7FD8FF, Alpha = 0.5f });
        var core = new ColorRect(new ColorRectConfig { X = (int)x, Y = (int)y, Width = w, Height = h, Color = 0xFFFFFF, Alpha = 1f });
        _fxLayer.AddChild(halo);
        _fxLayer.AddChild(core);
        strike.Rects.Add(halo);
        strike.Rects.Add(core);
    }

    private void UpdateStrikes(double ms) {
        for (var i = _strikes.Count - 1; i >= 0; i--) {
            var s = _strikes[i];
            var t = (float)(ms - s.Start) / StrikeLifeMs;
            if (t >= 1f) {
                foreach (var r in s.Rects) {
                    _fxLayer.RemoveChild(r);
                }
                _strikes.RemoveAt(i);
                continue;
            }

            // Quick flicker while it fades.
            var flicker = ((int)((ms - s.Start) / 40.0) % 2 == 0) ? 1f : 0.55f;
            var a = (1f - t) * flicker;
            for (var j = 0; j < s.Rects.Count; j++) {
                s.Rects[j].Alpha = (j % 2 == 0 ? 0.5f : 1f) * a;
            }
        }
    }

    // ---- Floating damage numbers -----------------------------------------------------------------------------------------
    private const int PopLifeMs = 800;

    private void AddPop(string text, float x, float y, uint color) {
        var label = new SimpleText(new TextConfig {
            Text = text,
            FontSize = 13f,
            FontType = FontType.Bold,
            FontGroup = FontGroup.MyriadPro,
            Color = color,
            OutlineColor = 0x120A05,
            OutlineThickness = 1,
            X = (int)x,
            Y = (int)y,
            Anchor = UiAnchor.Middle
        });
        _fxLayer.AddChild(label);
        _pops.Add(new Pop { Text = label, Start = _lastMs, X = x, Y = y });
    }

    private void UpdatePops(double ms) {
        for (var i = _pops.Count - 1; i >= 0; i--) {
            var p = _pops[i];
            var t = (float)(ms - p.Start) / PopLifeMs;
            if (t >= 1f) {
                _fxLayer.RemoveChild(p.Text);
                _pops.RemoveAt(i);
                continue;
            }

            p.Text.X = (int)p.X;
            p.Text.Y = (int)(p.Y - t * 30f);
            p.Text.Alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
        }
    }
}
