using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Media;

namespace KodaRacer
{
    public enum GameState { Ready, CarSelect, Playing, Results }

    public class RoadSprite
    {
        public float Offset;
        public bool IsPalm;
    }

    public class Segment
    {
        public int Index;
        public float Curve;
        public float Y1;
        public float Y2;
        public bool RumbleAlt;
        public bool HasLane;
        public List<RoadSprite> Sprites = new List<RoadSprite>();
    }

    public class TrafficCar
    {
        public int SegIndex;
        public float Z;
        public float Offset;
        public float Speed;
        public Color Color;
    }

    public class Checkpoint
    {
        public int Seg;
        public bool Passed;
    }

    class Particle
    {
        public float X, Y, Vx, Vy, Life, Age;
        public Color Color;
    }

    class Star
    {
        public float X, Y, R, Phase, Speed;
    }

    class Building
    {
        public float X, W, H;
        public int Wc, Wr;
        public Color?[] Windows;
    }

    struct ProjPoint
    {
        public float X, Y, W, Scale, CamZ;
    }

    public class Game1 : Game
    {
        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------
        GraphicsDeviceManager _graphics;
        SpriteBatch _spriteBatch;
        SpriteFont _font;
        Texture2D _pixel;
        Texture2D _logo;
        Song _menuSong;
        Song _raceSong;
        Song _carSelectSong;
        Song _resultsSong;
        BasicEffect _effect;
        Random _rng = new Random();

        int ScreenW => GraphicsDevice.Viewport.Width;
        int ScreenH => GraphicsDevice.Viewport.Height;

        // ------------------------------------------------------------------
        // Track / physics constants (mirrors koda.html)
        // ------------------------------------------------------------------
        const float RoadWidth = 2200f;
        const float SegmentLength = 200f;
        const int RumbleLength = 3;
        const int Lanes = 3;
        const float FieldOfView = 100f;
        const float CameraHeight = 1100f;
        float _cameraDepth;
        const int DrawDistance = 200;
        const float Centrifugal = 0.3f;

        float _maxSpeed, _accel, _brakePower, _decel, _offRoadDecel, _offRoadLimit;

        const float PlayerHalfWidth = 0.16f;
        const float CarHalfWidth = 0.22f;
        const float SpriteHalfWidth = 0.20f;
        float _carHitRadius, _spriteHitRadius;
        const float MaxSpriteOffset = 2.0f;
        float _playerClamp;

        // ------------------------------------------------------------------
        // World state
        // ------------------------------------------------------------------
        List<Segment> _segments = new List<Segment>();
        float _trackY;
        float _trackLength;
        List<TrafficCar> _cars = new List<TrafficCar>();
        List<Checkpoint> _checkpoints = new List<Checkpoint>();
        List<Particle> _particles = new List<Particle>();
        List<Star> _stars = new List<Star>();
        List<Building> _buildings = new List<Building>();

        GameState _state = GameState.Ready;
        float _position, _distanceTraveled, _playerX, _speed, _score;
        float _timeLeft = 45f;
        float _shake, _spinTimer, _flashTimer, _skyX;
        int _spinDir = 1;
        string _flashMsg = "";
        string _hudStageText = "";
        string _ovTitle = "KODA", _ovSub = "", _ovMsg = "";
        bool _ovShowMsg = false;
        bool _muted = false;
        double _nowMs = 0;
        KeyboardState _prevKs;
        GamePadState _prevGp;
        PlayerIndex _gpIndex = PlayerIndex.One;
        bool _gpConnected = false;
        int _carIndex = 0;

        static readonly Color[] CarColors =
        {
            new Color(255, 43, 214), new Color(255, 234, 0), new Color(57, 255, 136),
            new Color(255, 85, 0), new Color(125, 255, 234), new Color(200, 107, 255)
        };

        static readonly (Color Color, string Name)[] PlayerCars =
        {
            (new Color(0x3e, 0xf3, 0xff), "NEON CYAN"),
            (new Color(0xff, 0x2b, 0xd6), "HOT PINK"),
            (new Color(0xff, 0xea, 0x00), "VOLT YELLOW"),
            (new Color(0x39, 0xff, 0x88), "ACID GREEN"),
            (new Color(0xff, 0x55, 0x00), "SUNSET ORANGE"),
            (new Color(0xc8, 0x6b, 0xff), "ULTRAVIOLET")
        };

        static readonly Color RoadColor = new Color(0x1b, 0x0e, 0x33);
        static readonly Color GrassA = new Color(0x0a, 0x01, 0x19);
        static readonly Color GrassB = new Color(0x0d, 0x02, 0x21);
        static readonly Color RumbleA = new Color(0xff, 0x2b, 0xd6);
        static readonly Color RumbleB = new Color(0x3e, 0xf3, 0xff);
        static readonly Color LaneColor = new Color(0xea, 0xfc, 0xff);
        static readonly Color FogColor = new Color(17, 8, 33);

        static readonly int[,] CarPixels =
        {
            {0,0,0,1,1,1,0,0,0},
            {0,0,1,2,2,2,1,0,0},
            {0,1,1,2,2,2,1,1,0},
            {1,1,1,1,4,1,1,1,1},
            {1,1,4,1,1,1,4,1,1},
            {1,3,1,1,1,1,1,3,1},
            {0,1,0,0,0,0,0,1,0}
        };

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
            Window.Title = "KODA";
        }

        protected override void Initialize()
        {
            _graphics.PreferredBackBufferWidth = 1280;
            _graphics.PreferredBackBufferHeight = 720;
            Window.AllowUserResizing = true;
            Window.ClientSizeChanged += OnClientSizeChanged;

            _cameraDepth = 1f / MathF.Tan((FieldOfView / 2f) * MathF.PI / 180f);
            _maxSpeed = SegmentLength * 58f;
            _accel = _maxSpeed / 2.0f;
            _brakePower = -_maxSpeed * 1.6f;
            _decel = -_maxSpeed / 5f;
            _offRoadDecel = -_maxSpeed / 2.1f;
            _offRoadLimit = _maxSpeed / 3f;
            _carHitRadius = PlayerHalfWidth + CarHalfWidth;
            _spriteHitRadius = PlayerHalfWidth + SpriteHalfWidth;
            _playerClamp = MaxSpriteOffset + _spriteHitRadius + 0.4f;

            _graphics.ApplyChanges();
            base.Initialize();
        }

        void OnClientSizeChanged(object sender, EventArgs e)
        {
            if (GraphicsDevice == null) return;
            int w = Window.ClientBounds.Width;
            int h = Window.ClientBounds.Height;
            if (w <= 0 || h <= 0) return;
            _graphics.PreferredBackBufferWidth = w;
            _graphics.PreferredBackBufferHeight = h;
            _graphics.ApplyChanges();
            InitSkyline();
            InitStars();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _font = Content.Load<SpriteFont>("Fonts/Hud");
            _logo = Content.Load<Texture2D>("Sprites/koda_logo");
            _menuSong = Content.Load<Song>("Audio/koda_menu_theme");
            _raceSong = Content.Load<Song>("Audio/koda_race_theme");
            _carSelectSong = Content.Load<Song>("Audio/koda_car_select_theme");
            _resultsSong = Content.Load<Song>("Audio/koda_results_theme");

            _pixel = new Texture2D(GraphicsDevice, 1, 1);
            _pixel.SetData(new[] { Color.White });

            _effect = new BasicEffect(GraphicsDevice)
            {
                VertexColorEnabled = true,
                World = Matrix.Identity,
                View = Matrix.Identity
            };

            MediaPlayer.IsRepeating = true;
            MediaPlayer.Volume = 0.6f;

            InitSkyline();
            InitStars();
            BuildTrack();
            BuildCars();
            _speed = _maxSpeed * 0.1f;
            _hudStageText = "CHECKPOINT 0/" + _checkpoints.Count;
            ShowMenuOverlay("KODA", "");
            PlayMenuMusic();
        }

        // ------------------------------------------------------------------
        // Input helpers (keyboard + gamepad, both accepted)
        // ------------------------------------------------------------------
        const float StickDeadzone = 0.35f;
        const float TriggerDeadzone = 0.25f;

        // Xbox (and any XInput/SDL) controllers don't always land on slot 1 -
        // e.g. if another HID device grabbed that slot first. Scan all four
        // slots and stick with whichever one is actually connected.
        GamePadState GetGamePadState()
        {
            var current = GamePad.GetState(_gpIndex);
            if (current.IsConnected) return current;

            int max = Math.Min(4, GamePad.MaximumGamePadCount);
            for (int i = 0; i < max; i++)
            {
                var idx = (PlayerIndex)i;
                var gp = GamePad.GetState(idx);
                if (gp.IsConnected)
                {
                    _gpIndex = idx;
                    return gp;
                }
            }
            return current;
        }

        static bool GpUp(GamePadState gp) =>
            gp.IsButtonDown(Buttons.A) || gp.IsButtonDown(Buttons.DPadUp) ||
            gp.Triggers.Right > TriggerDeadzone || gp.ThumbSticks.Left.Y > StickDeadzone;

        static bool GpBrake(GamePadState gp) =>
            gp.IsButtonDown(Buttons.B) || gp.IsButtonDown(Buttons.X) || gp.IsButtonDown(Buttons.DPadDown) ||
            gp.Triggers.Left > TriggerDeadzone || gp.ThumbSticks.Left.Y < -StickDeadzone;

        static bool GpLeft(GamePadState gp) =>
            gp.IsButtonDown(Buttons.DPadLeft) || gp.ThumbSticks.Left.X < -StickDeadzone;

        static bool GpRight(GamePadState gp) =>
            gp.IsButtonDown(Buttons.DPadRight) || gp.ThumbSticks.Left.X > StickDeadzone;

        static bool KeyUp(KeyboardState ks, GamePadState gp) => ks.IsKeyDown(Keys.Up) || ks.IsKeyDown(Keys.W) || GpUp(gp);
        static bool KeyBrake(KeyboardState ks, GamePadState gp) => ks.IsKeyDown(Keys.Down) || ks.IsKeyDown(Keys.S) || GpBrake(gp);
        static bool KeyLeft(KeyboardState ks, GamePadState gp) => ks.IsKeyDown(Keys.Left) || ks.IsKeyDown(Keys.A) || GpLeft(gp);
        static bool KeyRight(KeyboardState ks, GamePadState gp) => ks.IsKeyDown(Keys.Right) || ks.IsKeyDown(Keys.D) || GpRight(gp);

        // ------------------------------------------------------------------
        // Math helpers (mirrors JS easing/percent helpers)
        // ------------------------------------------------------------------
        static float PercentRemaining(float n, float total) => (n % total) / total;
        static float Interpolate(float a, float b, float t) => a + (b - a) * t;
        static float EaseIn(float a, float b, float t) => a + (b - a) * (t * t);
        static float EaseInOut(float a, float b, float t) => a + (b - a) * ((-(float)Math.Cos(t * Math.PI) / 2f) + 0.5f);
        static float Increase(float start, float inc, float max)
        {
            float r = start + inc;
            while (r >= max) r -= max;
            while (r < 0) r += max;
            return r;
        }

        Segment FindSegment(float z)
        {
            int idx = (int)Math.Floor(z / SegmentLength) % _segments.Count;
            if (idx < 0) idx += _segments.Count;
            return _segments[idx];
        }

        // ------------------------------------------------------------------
        // Track building
        // ------------------------------------------------------------------
        void AddSegment(float curve, float y)
        {
            int n = _segments.Count;
            _segments.Add(new Segment
            {
                Index = n,
                Curve = curve,
                Y1 = _trackY,
                Y2 = y,
                RumbleAlt = (n / RumbleLength) % 2 == 1,
                HasLane = (n / RumbleLength) % 2 == 1
            });
            _trackY = y;
        }

        void AddRoad(int enter, int hold, int leave, float curve, float height)
        {
            float startY = _trackY;
            float endY = startY + height;
            int total = enter + hold + leave;
            for (int j = 0; j < total; j++)
            {
                float c;
                if (j < enter) c = EaseIn(0, curve, (float)j / enter);
                else if (j < enter + hold) c = curve;
                else c = EaseInOut(curve, 0, (float)(j - enter - hold) / leave);
                float y = EaseInOut(startY, endY, (float)j / total);
                AddSegment(c, y);
            }
        }

        void BuildTrack()
        {
            _segments = new List<Segment>();
            _trackY = 0;
            int target = 2000;

            var pieces = new List<Action>
            {
                () => AddRoad(50, 50, 50, 0, 0),
                () => AddRoad(40, 60, 40, 2.6f, 0),
                () => AddRoad(40, 60, 40, -2.6f, 0),
                () => AddRoad(30, 70, 30, 4.4f, 0),
                () => AddRoad(30, 70, 30, -4.4f, 0),
                () => AddRoad(60, 40, 60, 0, 450),
                () => AddRoad(60, 40, 60, 0, -450),
                () => AddRoad(50, 60, 50, 0, 850),
                () => AddRoad(50, 60, 50, 0, -850),
                () => AddRoad(40, 60, 40, 3.2f, 480),
                () => AddRoad(40, 60, 40, -3.2f, -480),
                () => AddRoad(30, 50, 30, 5, 650),
                () => AddRoad(30, 50, 30, -5, -650)
            };

            while (_segments.Count < target)
                pieces[_rng.Next(pieces.Count)]();

            if (_segments.Count > target)
                _segments.RemoveRange(target, _segments.Count - target);

            int flattenStart = _segments.Count - 60;
            float flattenStartY = flattenStart > 0 ? _segments[flattenStart - 1].Y2 : 0;
            float flattenStartCurve = _segments[flattenStart].Curve;
            for (int k = flattenStart; k < _segments.Count; k++)
            {
                float ft = (float)(k - flattenStart) / (_segments.Count - flattenStart);
                _segments[k].Y1 = (k == flattenStart) ? flattenStartY : _segments[k - 1].Y2;
                _segments[k].Y2 = EaseInOut(flattenStartY, 0, ft);
                _segments[k].Curve = EaseInOut(flattenStartCurve, 0, ft);
            }

            for (int s = 0; s < _segments.Count; s++)
            {
                if (s > 20 && s % 5 == 0 && _rng.NextDouble() < 0.8)
                {
                    int side = ((s / 5) % 2 == 0) ? -1 : 1;
                    bool isPalm = _rng.NextDouble() < 0.55;
                    float offset = side * (1.55f + (float)_rng.NextDouble() * 0.45f);
                    _segments[s].Sprites.Add(new RoadSprite { Offset = offset, IsPalm = isPalm });
                }
            }

            _trackLength = _segments.Count * SegmentLength;

            _checkpoints = new List<Checkpoint>();
            int cpCount = 7;
            for (int c = 1; c <= cpCount; c++)
                _checkpoints.Add(new Checkpoint { Seg = (int)((_segments.Count / (float)(cpCount + 1)) * c), Passed = false });
        }

        void BuildCars()
        {
            _cars = new List<TrafficCar>();
            int n = 20;
            for (int i = 0; i < n; i++)
            {
                int segIndex = 24 + _rng.Next(_segments.Count - 80);
                _cars.Add(new TrafficCar
                {
                    SegIndex = segIndex,
                    Z = segIndex * SegmentLength,
                    Offset = (float)_rng.NextDouble() * 1.7f - 0.85f,
                    Speed = _maxSpeed * (0.1f + (float)_rng.NextDouble() * 0.2f),
                    Color = CarColors[_rng.Next(CarColors.Length)]
                });
            }
        }

        // ------------------------------------------------------------------
        // Projection + primitive batching (stands in for canvas polygon fills)
        // ------------------------------------------------------------------
        ProjPoint Project(float worldX, float worldY, float worldZ, float cameraX, float cameraY, float cameraZ)
        {
            float camZ = worldZ - cameraZ;
            float clampedZ = camZ < 1 ? 1 : camZ;
            float scale = _cameraDepth / clampedZ;
            return new ProjPoint
            {
                X = (ScreenW / 2f) + (scale * (worldX - cameraX) * ScreenW / 2f),
                Y = (ScreenH / 2f) - (scale * (worldY - cameraY) * ScreenH / 2f),
                W = scale * RoadWidth * ScreenW / 2f,
                Scale = scale,
                CamZ = camZ
            };
        }

        List<VertexPositionColor> _verts = new List<VertexPositionColor>(4096);

        void Tri(Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            _verts.Add(new VertexPositionColor(new Vector3(a, 0), color));
            _verts.Add(new VertexPositionColor(new Vector3(b, 0), color));
            _verts.Add(new VertexPositionColor(new Vector3(c, 0), color));
        }

        void TriGrad(Vector2 a, Vector2 b, Vector2 c, Color ca, Color cb, Color cc)
        {
            _verts.Add(new VertexPositionColor(new Vector3(a, 0), ca));
            _verts.Add(new VertexPositionColor(new Vector3(b, 0), cb));
            _verts.Add(new VertexPositionColor(new Vector3(c, 0), cc));
        }

        void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            Tri(a, b, c, color);
            Tri(a, c, d, color);
        }

        void QuadGrad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color ca, Color cb, Color cc, Color cd)
        {
            TriGrad(a, b, c, ca, cb, cc);
            TriGrad(a, c, d, ca, cc, cd);
        }

        void QuadRect(float x, float y, float w, float h, Color color)
        {
            Quad(new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h), color);
        }

        void Line(Vector2 p1, Vector2 p2, float thickness, Color color)
        {
            Vector2 dir = p2 - p1;
            float len = dir.Length();
            if (len < 0.0001f) return;
            Vector2 n = new Vector2(-dir.Y, dir.X) / len * (thickness / 2f);
            Quad(p1 - n, p2 - n, p2 + n, p1 + n, color);
        }

        void Circle(Vector2 center, float radius, Color color, int segments = 28)
        {
            float step = MathHelper.TwoPi / segments;
            Vector2 prev = center + new Vector2(radius, 0);
            for (int i = 1; i <= segments; i++)
            {
                float ang = step * i;
                Vector2 cur = center + new Vector2((float)Math.Cos(ang) * radius, (float)Math.Sin(ang) * radius);
                Tri(center, prev, cur, color);
                prev = cur;
            }
        }

        void FlushTriangles()
        {
            if (_verts.Count == 0) return;
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, _verts.ToArray(), 0, _verts.Count / 3);
            }
            _verts.Clear();
        }

        static Color WithFog(Color c, float amt) => Color.Lerp(c, FogColor, MathHelper.Clamp(amt, 0, 1));
        static Color WithAlpha(Color c, float mul) => new Color(c.R, c.G, c.B, (int)MathHelper.Clamp(c.A * mul, 0, 255));

        // ------------------------------------------------------------------
        // Road segment drawing
        // ------------------------------------------------------------------
        void DrawSegment(ProjPoint p1, ProjPoint p2, bool rumbleAlt, bool hasLane, int index, float fogAmt)
        {
            Color grass = WithFog(((index / (RumbleLength * 3)) % 2 == 1) ? GrassA : GrassB, fogAmt);
            Quad(new Vector2(0, p1.Y), new Vector2(ScreenW, p1.Y), new Vector2(ScreenW, p2.Y), new Vector2(0, p2.Y), grass);

            Color rumbleColor = WithFog(rumbleAlt ? RumbleA : RumbleB, fogAmt);
            float r1 = p1.W * 0.18f, r2 = p2.W * 0.18f;

            Quad(new Vector2(p1.X - p1.W - r1, p1.Y), new Vector2(p1.X - p1.W, p1.Y), new Vector2(p2.X - p2.W, p2.Y), new Vector2(p2.X - p2.W - r2, p2.Y), rumbleColor);
            Quad(new Vector2(p1.X + p1.W, p1.Y), new Vector2(p1.X + p1.W + r1, p1.Y), new Vector2(p2.X + p2.W + r2, p2.Y), new Vector2(p2.X + p2.W, p2.Y), rumbleColor);

            Quad(new Vector2(p1.X - p1.W, p1.Y), new Vector2(p1.X + p1.W, p1.Y), new Vector2(p2.X + p2.W, p2.Y), new Vector2(p2.X - p2.W, p2.Y), WithFog(RoadColor, fogAmt));

            if (hasLane)
            {
                float lw1 = (p1.W * 2) / (Lanes * 6), lw2 = (p2.W * 2) / (Lanes * 6);
                Color fogLane = WithFog(LaneColor, fogAmt);
                for (int l = 1; l < Lanes; l++)
                {
                    float lx1 = p1.X - p1.W + (p1.W * 2 / Lanes) * l;
                    float lx2 = p2.X - p2.W + (p2.W * 2 / Lanes) * l;
                    Quad(new Vector2(lx1 - lw1 / 2, p1.Y), new Vector2(lx1 + lw1 / 2, p1.Y), new Vector2(lx2 + lw2 / 2, p2.Y), new Vector2(lx2 - lw2 / 2, p2.Y), fogLane);
                }
            }
        }

        // ------------------------------------------------------------------
        // Pixel-art sprites
        // ------------------------------------------------------------------
        void DrawPixelCar(float cx, float baseY, float totalW, Color bodyColor, float rotation, float alpha = 1f)
        {
            int cols = 9, rows = 7;
            float scale = Math.Max(1, totalW / cols);
            float h = rows * scale;
            float left = -(cols * scale) / 2f;
            float top = -h;
            float cosr = (float)Math.Cos(rotation), sinr = (float)Math.Sin(rotation);

            Vector2 T(float lx, float ly)
            {
                float rx = lx * cosr - ly * sinr;
                float ry = lx * sinr + ly * cosr;
                return new Vector2(cx + rx, baseY + ry);
            }

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int v = CarPixels[r, c];
                    if (v == 0) continue;
                    Color col = v == 1 ? bodyColor : (v == 2 ? new Color(0xbf, 0xf9, 0xff) : (v == 3 ? new Color(0xff, 0xea, 0x00) : Color.White));
                    col = WithAlpha(col, alpha);
                    float px = left + c * scale, py = top + r * scale;
                    float cw = scale + 1, ch = scale + 1;
                    Quad(T(px, py), T(px + cw, py), T(px + cw, py + ch), T(px, py + ch), col);
                }
            }

            Color shadow = WithAlpha(new Color(0, 0, 0, 90), alpha);
            float shH = Math.Max(2, scale * 0.35f);
            Quad(T(left, 0), T(left + cols * scale, 0), T(left + cols * scale, shH), T(left, shH), shadow);
        }

        void DrawPalm(float x, float baseY, float scale, float wobble, float alpha = 1f)
        {
            float trunkW = Math.Max(2, scale * 0.9f), trunkH = scale * 7.5f;
            Color trunkColor = WithAlpha(new Color(0x22, 0x10, 0x3a), alpha);
            for (int i = 0; i < 6; i++)
            {
                float seg = trunkH / 6f;
                float sway = (float)Math.Sin(i * 0.5f + wobble) * scale * 0.3f;
                QuadRect(x - trunkW / 2f + sway, baseY - trunkH + i * seg, trunkW, seg + 1, trunkColor);
            }

            float topX = x + (float)Math.Sin(wobble) * scale * 0.3f;
            float topY = baseY - trunkH;
            Color frondColor = WithAlpha(new Color(0xff, 0x2b, 0xd6), alpha);
            for (int f = 0; f < 5; f++)
            {
                float ang = (-MathF.PI * 0.8f) + (f / 4f) * MathF.PI * 0.9f;
                float fx = topX + (float)Math.Cos(ang) * scale * 3.4f;
                float fy = topY + (float)Math.Sin(ang) * scale * 1.8f;
                Vector2 p0 = new Vector2(topX, topY);
                Vector2 p1 = new Vector2(topX + (fx - topX) * 0.6f + scale * 0.4f, topY + (fy - topY) * 0.6f);
                Vector2 p2 = new Vector2(fx, fy);
                Tri(p0, p1, p2, frondColor);
            }

            Color crown = WithAlpha(new Color(0x3e, 0xf3, 0xff), alpha);
            QuadRect(topX - scale * 0.6f, topY - scale * 0.4f, scale * 1.2f, scale * 0.9f, crown);
        }

        void DrawPylon(float x, float baseY, float scale, float glowPhase, float alpha = 1f)
        {
            float poleW = Math.Max(2, scale * 0.6f), poleH = scale * 8f;
            QuadRect(x - poleW / 2f, baseY - poleH, poleW, poleH, WithAlpha(new Color(0x20, 0x12, 0x2f), alpha));

            float glow = 0.6f + (float)Math.Sin(glowPhase) * 0.4f;
            Color glowColor = WithAlpha(new Color(62, 243, 255, (int)(glow * 255)), alpha);
            QuadRect(x - scale * 1.6f, baseY - poleH - scale * 1.6f, scale * 3.2f, scale * 1.6f, glowColor);
            QuadRect(x - scale * 1.2f, baseY - poleH - scale * 1.2f, scale * 2.4f, scale * 0.7f, WithAlpha(new Color(0xff, 0xea, 0x00), alpha));
        }

        // ------------------------------------------------------------------
        // Background: sky, stars, sun, skyline
        // ------------------------------------------------------------------
        void InitSkyline()
        {
            _buildings.Clear();
            float x = 0;
            while (x < ScreenW * 2.2f)
            {
                float bw = 26 + (float)_rng.NextDouble() * 46f;
                float bh = 60 + (float)_rng.NextDouble() * 200f;
                int wc = (int)(bw / 9), wr = (int)(bh / 14);
                var windows = new Color?[Math.Max(0, wc * wr)];
                for (int i = 0; i < windows.Length; i++)
                    windows[i] = _rng.NextDouble() < 0.35 ? (_rng.NextDouble() < 0.5 ? new Color(0x3e, 0xf3, 0xff) : new Color(0xff, 0x2b, 0xd6)) : (Color?)null;
                _buildings.Add(new Building { X = x, W = bw, H = bh, Wc = wc, Wr = wr, Windows = windows });
                x += bw + 4;
            }
        }

        void InitStars()
        {
            _stars.Clear();
            int count = Math.Max(40, (ScreenW * ScreenH) / 14000);
            for (int i = 0; i < count; i++)
            {
                _stars.Add(new Star
                {
                    X = (float)_rng.NextDouble() * ScreenW,
                    Y = (float)_rng.NextDouble() * ScreenH * 0.42f,
                    R = 0.6f + (float)_rng.NextDouble() * 1.6f,
                    Phase = (float)_rng.NextDouble() * MathF.PI * 2,
                    Speed = 1 + (float)_rng.NextDouble() * 2
                });
            }
        }

        void DrawSky(float horizon)
        {
            Color c0 = new Color(0x0b, 0x00, 0x33);
            Color c1 = new Color(0x2b, 0x0a, 0x63);
            Color c2 = new Color(0xff, 0x2b, 0xd6);
            float midY = horizon * 0.55f;
            QuadGrad(new Vector2(0, 0), new Vector2(ScreenW, 0), new Vector2(ScreenW, midY), new Vector2(0, midY), c0, c0, c1, c1);
            QuadGrad(new Vector2(0, midY), new Vector2(ScreenW, midY), new Vector2(ScreenW, horizon), new Vector2(0, horizon), c1, c1, c2, c2);
        }

        void DrawStars(float horizon)
        {
            foreach (var st in _stars)
            {
                if (st.Y > horizon - 4) continue;
                float tw = 0.5f + (float)Math.Sin(_nowMs * 0.002 * st.Speed + st.Phase) * 0.5f;
                int a = (int)MathHelper.Clamp((0.3f + tw * 0.7f) * 255f, 0, 255);
                QuadRect(st.X, st.Y, st.R, st.R, new Color(0xea, 0xfc, 0xff, a));
            }
        }

        void DrawSun(float horizon)
        {
            float sunX = ScreenW / 2f;
            float sunY = horizon - Math.Min(80, ScreenH * 0.08f);
            float sunR = Math.Min(120, ScreenW * 0.11f);

            for (int ray = 0; ray < 14; ray++)
            {
                float rAng = (ray / 14f) * MathF.PI * 2f + (float)(_nowMs * 0.00015);
                Vector2 p1 = new Vector2(sunX + (float)Math.Cos(rAng) * sunR * 1.05f, sunY + (float)Math.Sin(rAng) * sunR * 1.05f);
                Vector2 p2 = new Vector2(sunX + (float)Math.Cos(rAng) * sunR * 1.9f, sunY + (float)Math.Sin(rAng) * sunR * 1.9f);
                Line(p1, p2, Math.Max(1, sunR * 0.02f), new Color(0xff, 0xea, 0x00, 64));
            }

            Circle(new Vector2(sunX, sunY), sunR, new Color(0xff, 0x8a, 0x00));
            Circle(new Vector2(sunX, sunY), sunR * 0.62f, new Color(0xff, 0xea, 0x00));

            int bandCount = 6;
            Color bandColor = new Color(0x0b, 0x00, 0x33);
            for (int b = 0; b < bandCount; b++)
            {
                float relY = (-sunR + sunR * 0.55f) + b * (sunR / 8.5f);
                float bandH = Math.Max(2, sunR * 0.045f);
                float chordTop = sunR * sunR - relY * relY;
                float chordBot = sunR * sunR - (relY + bandH) * (relY + bandH);
                float minChordSq = Math.Min(chordTop, chordBot);
                if (minChordSq <= 0) continue;
                float halfW = (float)Math.Sqrt(minChordSq);
                QuadRect(sunX - halfW, sunY + relY, halfW * 2, bandH, bandColor);
            }
        }

        void DrawSkyline(float horizon, float curve)
        {
            _skyX = Increase(_skyX, curve * 2f - _playerX * 1.2f, 100000f);
            float offset = -(_skyX % ScreenW);
            float clipTop = horizon - 170;

            for (int pass = 0; pass < 2; pass++)
            {
                float ox = offset + pass * ScreenW;
                foreach (var bd in _buildings)
                {
                    float bx = bd.X + ox;
                    if (bx > ScreenW || bx + bd.W < -ScreenW) continue;
                    float by = horizon - bd.H;
                    float visTop = Math.Max(by, clipTop);
                    float visH = (by + bd.H) - visTop;
                    if (visH > 0)
                        QuadRect(bx, visTop, bd.W, visH, new Color(0x1a, 0x0a, 0x2e));

                    for (int wy = 0; wy < bd.Wr; wy++)
                    {
                        float wyPix = by + 6 + wy * 14;
                        if (wyPix < clipTop) continue;
                        for (int wx = 0; wx < bd.Wc; wx++)
                        {
                            var col = bd.Windows[wy * bd.Wc + wx];
                            if (col == null) continue;
                            QuadRect(bx + 4 + wx * 9, wyPix, 4, 6, col.Value);
                        }
                    }
                }
            }
        }

        void DrawBackground(float curve, float hillTilt)
        {
            float horizon = ScreenH * 0.5f + MathHelper.Clamp(hillTilt, -40, 40);
            DrawSky(horizon);
            DrawStars(horizon);
            DrawSun(horizon);
            DrawSkyline(horizon, curve);
            QuadRect(0, horizon - 2, ScreenW, 4, new Color(0x14, 0x08, 0x26));
        }

        // ------------------------------------------------------------------
        // Particles / speed lines / player car
        // ------------------------------------------------------------------
        void SpawnDust(float cx, float y, bool offroad)
        {
            if (_particles.Count > 70) return;
            for (int i = 0; i < 2; i++)
            {
                _particles.Add(new Particle
                {
                    X = cx + ((float)_rng.NextDouble() * 20 - 10),
                    Y = y - (float)_rng.NextDouble() * 4,
                    Vx = ((float)_rng.NextDouble() * 2 - 1) * 30,
                    Vy = -20 - (float)_rng.NextDouble() * 30,
                    Life = 0.5f + (float)_rng.NextDouble() * 0.3f,
                    Age = 0,
                    Color = offroad ? new Color(0x8a, 0x6b, 0x3a) : new Color(0x3e, 0xf3, 0xff)
                });
            }
        }

        void UpdateParticles(float dt)
        {
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];
                p.Age += dt;
                if (p.Age >= p.Life) { _particles.RemoveAt(i); continue; }
                p.X += p.Vx * dt;
                p.Y += p.Vy * dt;
                p.Vy += 60 * dt;
            }
        }

        void DrawParticles()
        {
            foreach (var p in _particles)
            {
                float a = MathHelper.Clamp(1 - (p.Age / p.Life), 0, 1) * 0.8f;
                Color c = new Color(p.Color.R, p.Color.G, p.Color.B, (int)(a * 255));
                QuadRect(p.X, p.Y, 4, 4, c);
            }
        }

        void DrawSpeedLines(float intensity)
        {
            if (intensity <= 0) return;
            float cx = ScreenW / 2f, cy = ScreenH * 0.42f;
            int n = 10;
            int alpha = (int)(MathHelper.Clamp(intensity, 0, 1) * 0.35f * 255);
            Color col = new Color(0xea, 0xfc, 0xff, alpha);
            for (int i = 0; i < n; i++)
            {
                float ang = (i / (float)n) * MathF.PI * 2f + _skyX * 0.001f;
                float r1 = 40, r2 = 40 + intensity * 220;
                Vector2 p1 = new Vector2(cx + (float)Math.Cos(ang) * r1, cy + (float)Math.Sin(ang) * r1 * 0.5f);
                Vector2 p2 = new Vector2(cx + (float)Math.Cos(ang) * r2, cy + (float)Math.Sin(ang) * r2 * 0.5f);
                Line(p1, p2, 2, col);
            }
        }

        void DrawPlayerCar(KeyboardState ks, GamePadState gp)
        {
            float steer = 0;
            if (KeyLeft(ks, gp)) steer = -1;
            if (KeyRight(ks, gp)) steer = 1;
            float lean = steer * 6;
            float rotation;
            if (_spinTimer > 0)
            {
                float t = 1 - MathHelper.Clamp(_spinTimer / 1.0f, 0, 1);
                rotation = t * MathF.PI * 2f * _spinDir;
                lean = 0;
            }
            else
            {
                rotation = steer * 0.1f;
            }

            float shakeOff = _shake > 0 ? ((float)_rng.NextDouble() * _shake - _shake / 2f) : 0;
            float cx = ScreenW / 2f + lean + shakeOff;
            float baseY = ScreenH - Math.Max(24, ScreenH * 0.05f);
            float carW = MathHelper.Clamp(ScreenW * 0.14f, 110, 190);

            if (_spinTimer <= 0)
            {
                Color beamNear = new Color(255, 245, 200, 71);
                Color beamFar = new Color(255, 245, 200, 0);
                TriGrad(
                    new Vector2(cx - carW * 0.32f, baseY - carW * 0.7f),
                    new Vector2(cx - carW * 0.7f, baseY - carW * 2.1f),
                    new Vector2(cx - carW * 0.05f, baseY - carW * 2.1f),
                    beamNear, beamFar, beamFar);
                TriGrad(
                    new Vector2(cx + carW * 0.32f, baseY - carW * 0.7f),
                    new Vector2(cx + carW * 0.7f, baseY - carW * 2.1f),
                    new Vector2(cx + carW * 0.05f, baseY - carW * 2.1f),
                    beamNear, beamFar, beamFar);
            }

            Color playerColor = PlayerCars[_carIndex].Color;
            DrawPixelCar(cx, baseY, carW, playerColor, rotation);
            QuadRect(cx - carW * 0.35f, baseY + 4, carW * 0.7f, 5, new Color((int)playerColor.R, (int)playerColor.G, (int)playerColor.B, 128));
        }

        // ------------------------------------------------------------------
        // Cars / collisions / checkpoints
        // ------------------------------------------------------------------
        void UpdateCars(float dt)
        {
            foreach (var c in _cars)
            {
                c.Z = Increase(c.Z, dt * c.Speed, _trackLength);
                c.SegIndex = (int)Math.Floor(c.Z / SegmentLength) % _segments.Count;
            }
        }

        (int segPrev, int span, int segCount) CollisionSpan(float prevPosition)
        {
            int segCount = _segments.Count;
            int segNow = (int)Math.Floor(_position / SegmentLength) % segCount;
            int segPrev = (int)Math.Floor(prevPosition / SegmentLength) % segCount;
            if (segNow < 0) segNow += segCount;
            if (segPrev < 0) segPrev += segCount;
            int span = segNow - segPrev;
            if (span < 0) span += segCount;
            span = Math.Min(span, 4);
            return (segPrev, span, segCount);
        }

        TrafficCar CheckCollision(float prevPosition)
        {
            var (segPrev, span, segCount) = CollisionSpan(prevPosition);
            for (int d = 0; d <= span; d++)
            {
                int idx = (segPrev + d) % segCount;
                foreach (var c in _cars)
                    if (c.SegIndex == idx && Math.Abs(c.Offset - _playerX) < _carHitRadius) return c;
            }
            return null;
        }

        RoadSprite CheckSpriteCollision(float prevPosition)
        {
            var (segPrev, span, segCount) = CollisionSpan(prevPosition);
            for (int d = 0; d <= span; d++)
            {
                int idx = (segPrev + d) % segCount;
                var seg = _segments[idx];
                foreach (var sp in seg.Sprites)
                    if (Math.Abs(sp.Offset - _playerX) < _spriteHitRadius) return sp;
            }
            return null;
        }

        void CheckCheckpoints()
        {
            int idx = (int)(_distanceTraveled / SegmentLength);
            for (int i = 0; i < _checkpoints.Count; i++)
            {
                var cp = _checkpoints[i];
                if (!cp.Passed && idx >= cp.Seg)
                {
                    cp.Passed = true;
                    _timeLeft += 12;
                    _score += 500;
                    _flashMsg = "CHECKPOINT +12s";
                    _flashTimer = 1.6f;
                    _hudStageText = "CHECKPOINT " + (i + 1) + "/" + _checkpoints.Count;
                }
            }
        }

        // ------------------------------------------------------------------
        // Menu / music
        // ------------------------------------------------------------------
        void PlayMenuMusic()
        {
            MediaPlayer.Play(_menuSong);
        }

        void PlayRaceMusic()
        {
            MediaPlayer.Play(_raceSong);
        }

        void PlayCarSelectMusic()
        {
            MediaPlayer.Play(_carSelectSong);
        }

        void PlayResultsMusic()
        {
            MediaPlayer.Play(_resultsSong);
        }

        void ShowMenuOverlay(string title, string sub)
        {
            _ovTitle = title;
            _ovSub = sub;
            _ovShowMsg = false;
            _ovMsg = "";
        }

        void ShowResultsOverlay(string title, string sub)
        {
            _ovTitle = title;
            _ovSub = sub;
            _ovShowMsg = false;
            _ovMsg = "";
        }

        int CheckpointsPassed()
        {
            int n = 0;
            foreach (var cp in _checkpoints)
                if (cp.Passed) n++;
            return n;
        }

        void StartGame()
        {
            BuildTrack();
            BuildCars();
            _position = 0; _playerX = 0; _speed = _maxSpeed * 0.3f;
            _distanceTraveled = 0; _score = 0; _shake = 0; _spinTimer = 0;
            _timeLeft = 45;
            _particles.Clear();
            _hudStageText = "CHECKPOINT 0/" + _checkpoints.Count;
            _state = GameState.Playing;
        }

        // ------------------------------------------------------------------
        // Update
        // ------------------------------------------------------------------
        protected override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (dt > 0.05f) dt = 0.05f;
            _nowMs = gameTime.TotalGameTime.TotalMilliseconds;

            var ks = Keyboard.GetState();
            var gp = GetGamePadState();
            _gpConnected = gp.IsConnected;
            bool enterPressed = (ks.IsKeyDown(Keys.Enter) && !_prevKs.IsKeyDown(Keys.Enter))
                || (gp.IsButtonDown(Buttons.Start) && !_prevGp.IsButtonDown(Buttons.Start))
                || (gp.IsButtonDown(Buttons.A) && !_prevGp.IsButtonDown(Buttons.A));
            bool mPressed = (ks.IsKeyDown(Keys.M) && !_prevKs.IsKeyDown(Keys.M))
                || (gp.IsButtonDown(Buttons.Back) && !_prevGp.IsButtonDown(Buttons.Back))
                || (gp.IsButtonDown(Buttons.Y) && !_prevGp.IsButtonDown(Buttons.Y));
            bool leftEdge = KeyLeft(ks, gp) && !KeyLeft(_prevKs, _prevGp);
            bool rightEdge = KeyRight(ks, gp) && !KeyRight(_prevKs, _prevGp);

            if (enterPressed)
            {
                if (_state == GameState.Ready)
                {
                    _state = GameState.CarSelect;
                    ShowMenuOverlay("CHOOSE YOUR CAR", "left/right to browse, enter to confirm");
                    PlayCarSelectMusic();
                }
                else if (_state == GameState.CarSelect)
                {
                    StartGame();
                    PlayRaceMusic();
                }
                else if (_state == GameState.Results)
                {
                    _state = GameState.Ready;
                    ShowMenuOverlay("KODA", "");
                    PlayMenuMusic();
                }
            }

            if (_state == GameState.CarSelect)
            {
                if (leftEdge) _carIndex = (_carIndex - 1 + PlayerCars.Length) % PlayerCars.Length;
                if (rightEdge) _carIndex = (_carIndex + 1) % PlayerCars.Length;
            }

            if (mPressed)
            {
                _muted = !_muted;
                MediaPlayer.IsMuted = _muted;
            }

            UpdateParticles(dt);

            if (_state == GameState.Playing)
                UpdateGame(dt, ks, gp);
            else
                GamePad.SetVibration(_gpIndex, 0f, 0f);

            _prevKs = ks;
            _prevGp = gp;
            base.Update(gameTime);
        }

        void UpdateGame(float dt, KeyboardState ks, GamePadState gp)
        {
            var baseSegment = FindSegment(_position);

            _timeLeft -= dt;
            if (_timeLeft <= 0)
            {
                _timeLeft = 0;
                _speed = 0;
                _state = GameState.Results;
                ShowResultsOverlay("TIME UP", "the neon fades...");
                PlayResultsMusic();
                return;
            }

            if (_spinTimer > 0)
            {
                _spinTimer = Math.Max(0, _spinTimer - dt);
                _speed += _decel * dt;
                _speed = MathHelper.Clamp(_speed, 0, _maxSpeed);
            }
            else
            {
                if (KeyUp(ks, gp)) _speed += _accel * dt;
                else if (KeyBrake(ks, gp)) _speed += _brakePower * dt;
                else _speed += _decel * dt;

                if ((_playerX < -1 || _playerX > 1) && _speed > _offRoadLimit)
                {
                    _speed += _offRoadDecel * dt;
                    SpawnDust(ScreenW / 2f, ScreenH - 20, true);
                }

                _speed = MathHelper.Clamp(_speed, 0, _maxSpeed);

                float steerPower = 0.35f + 0.65f * (_speed / _maxSpeed);
                float dxSteer = dt * 2.4f * steerPower;
                bool steering = false;
                if (KeyLeft(ks, gp)) { _playerX -= dxSteer; steering = true; }
                if (KeyRight(ks, gp)) { _playerX += dxSteer; steering = true; }
                if (steering && _speed > _maxSpeed * 0.5f) SpawnDust(ScreenW / 2f, ScreenH - 20, false);

                _playerX -= dxSteer * (_speed / _maxSpeed) * baseSegment.Curve * Centrifugal;
                _playerX = MathHelper.Clamp(_playerX, -_playerClamp, _playerClamp);
            }

            float prevPosition = _position;
            float moveAmount = dt * _speed;
            _position = Increase(_position, moveAmount, _trackLength);
            _distanceTraveled += moveAmount;
            _score += dt * _speed * 0.012f;

            UpdateCars(dt);
            CheckCheckpoints();

            if (_spinTimer <= 0)
            {
                var hit = CheckCollision(prevPosition);
                if (hit != null)
                {
                    _speed *= 0.2f;
                    _spinTimer = 1.0f;
                    _spinDir = (hit.Offset < _playerX) ? 1 : -1;
                    _playerX = MathHelper.Clamp(hit.Offset + _spinDir * (_carHitRadius + 0.15f), -_playerClamp, _playerClamp);
                    _shake = 22;
                }
                else
                {
                    var hitSprite = CheckSpriteCollision(prevPosition);
                    if (hitSprite != null)
                    {
                        _speed *= 0.1f;
                        _spinTimer = 1.3f;
                        _spinDir = (hitSprite.Offset < _playerX) ? 1 : -1;
                        _playerX = MathHelper.Clamp(hitSprite.Offset + _spinDir * (_spriteHitRadius + 0.15f), -_playerClamp, _playerClamp);
                        _shake = 26;
                        _flashMsg = "CRASH!";
                        _flashTimer = 1.0f;
                    }
                }
            }

            if (_distanceTraveled >= _trackLength)
            {
                _state = GameState.Results;
                _score += (float)Math.Floor(_timeLeft) * 100;
                ShowResultsOverlay("GOAL!", "stage clear");
                PlayResultsMusic();
            }

            if (_shake > 0) _shake = Math.Max(0, _shake - dt * 40);
            if (_flashTimer > 0) _flashTimer = Math.Max(0, _flashTimer - dt);

            float vib = MathHelper.Clamp(_shake / 30f, 0, 1);
            GamePad.SetVibration(_gpIndex, vib * 0.7f, vib * 0.4f);
        }

        // ------------------------------------------------------------------
        // Draw
        // ------------------------------------------------------------------
        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(new Color(5, 1, 15));
            _verts.Clear();

            var baseSegment = FindSegment(_position);
            float basePercent = PercentRemaining(_position, SegmentLength);
            float playerY = Interpolate(baseSegment.Y1, baseSegment.Y2, basePercent);
            float cameraY = CameraHeight + playerY;
            float hillTilt = -(baseSegment.Y2 - baseSegment.Y1) * 0.02f;

            DrawBackground(baseSegment.Curve, hillTilt);

            float x = 0, dx = -(baseSegment.Curve * basePercent);
            var renderList = new List<(Segment seg, ProjPoint p1, ProjPoint p2, int n)>();
            int maxn = Math.Min(DrawDistance, _segments.Count - 2);

            for (int n = 0; n < maxn; n++)
            {
                var segment = _segments[(baseSegment.Index + n) % _segments.Count];
                bool looped = segment.Index < baseSegment.Index;
                float segZ = segment.Index * SegmentLength + (looped ? _trackLength : 0);
                float camZBase = _position - (looped ? _trackLength : 0);

                var p1 = Project(0, segment.Y1, segZ, _playerX * RoadWidth - x, cameraY, camZBase);
                x += dx;
                dx += segment.Curve;
                var p2 = Project(0, segment.Y2, segZ + SegmentLength, _playerX * RoadWidth - x, cameraY, camZBase);

                if (p1.CamZ <= 1 || p2.CamZ <= 1) continue;
                renderList.Add((segment, p1, p2, n));
            }

            for (int i = renderList.Count - 1; i >= 0; i--)
            {
                var r = renderList[i];
                float fogAmt = MathHelper.Clamp((r.n / (float)DrawDistance - 0.5f) / 0.5f, 0, 1);
                fogAmt *= fogAmt;
                DrawSegment(r.p1, r.p2, r.seg.RumbleAlt, r.seg.HasLane, r.seg.Index, fogAmt);
            }

            for (int i = renderList.Count - 1; i >= 0; i--)
            {
                var r = renderList[i];
                var seg = r.seg;
                float spriteFog = MathHelper.Clamp((r.n / (float)DrawDistance - 0.55f) / 0.45f, 0, 1);
                float alphaMul = 1 - spriteFog * 0.85f;

                foreach (var sp in seg.Sprites)
                {
                    if (r.p1.W < 2) continue;
                    float spx = r.p1.X + sp.Offset * r.p1.W;
                    float spScale = MathHelper.Clamp(r.p1.W * 0.045f, 0.6f, 30f);
                    if (sp.IsPalm) DrawPalm(spx, r.p1.Y, spScale, _position * 0.002f + seg.Index, alphaMul);
                    else DrawPylon(spx, r.p1.Y, spScale, _position * 0.01f + seg.Index, alphaMul);
                }

                foreach (var car in _cars)
                {
                    if (car.SegIndex == seg.Index)
                    {
                        float scaleW = r.p1.W;
                        if (scaleW < 2) continue;
                        float carX = r.p1.X + car.Offset * r.p1.W;
                        float spriteW = MathHelper.Clamp(scaleW * 0.5f, 6, 170);
                        DrawPixelCar(carX, r.p1.Y, spriteW, car.Color, 0, alphaMul);
                    }
                }
            }

            float speedRatio = _speed / _maxSpeed;
            DrawSpeedLines(speedRatio > 0.6f ? (speedRatio - 0.6f) / 0.4f : 0);

            var ks = Keyboard.GetState();
            var gp = GetGamePadState();
            if (_state == GameState.Playing || _state == GameState.Results) DrawPlayerCar(ks, gp);
            DrawParticles();

            if (_shake > 0.5f)
            {
                int a = (int)(Math.Min(0.25f, _shake / 60f) * 255);
                QuadRect(0, 0, ScreenW, ScreenH, new Color(255, 0, 80, a));
            }

            GraphicsDevice.BlendState = BlendState.NonPremultiplied;
            GraphicsDevice.DepthStencilState = DepthStencilState.None;
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            _effect.Projection = Matrix.CreateOrthographicOffCenter(0, ScreenW, ScreenH, 0, -1, 1);
            FlushTriangles();

            _spriteBatch.Begin(blendState: BlendState.AlphaBlend);

            if (_flashTimer > 0)
            {
                float a = MathHelper.Clamp(_flashTimer / 1.6f, 0, 1);
                float fontScale = MathHelper.Clamp(ScreenW * 0.03f, 18, 46) / _font.LineSpacing;
                var sz = _font.MeasureString(_flashMsg) * fontScale;
                var pos = new Vector2(ScreenW / 2f - sz.X / 2f, ScreenH * 0.3f);
                _spriteBatch.DrawString(_font, _flashMsg, pos, new Color(255, 234, 0, (int)(a * 255)), 0, Vector2.Zero, fontScale, SpriteEffects.None, 0);
            }

            DrawHud();
            if (_state == GameState.Ready) DrawMenu();
            else if (_state == GameState.CarSelect) DrawCarSelect();
            else if (_state == GameState.Results) DrawResults();

            _spriteBatch.End();

            base.Draw(gameTime);
        }

        void DrawHud()
        {
            float pad = MathHelper.Clamp(ScreenW * 0.02f, 10, 28);
            float fs = MathHelper.Clamp(ScreenW * 0.013f, 10, 16);
            float scale = fs / _font.LineSpacing;

            string scoreTxt = "SCORE " + ((int)_score).ToString("D6");
            _spriteBatch.DrawString(_font, scoreTxt, new Vector2(pad, pad), new Color(0xff, 0x2b, 0xd6), 0, Vector2.Zero, scale, SpriteEffects.None, 0);

            string timeTxt = _timeLeft.ToString("0.0");
            float timeScale = MathHelper.Clamp(ScreenW * 0.026f, 20, 32) / _font.LineSpacing;
            var timeSize = _font.MeasureString(timeTxt) * timeScale;
            Color timeColor = _timeLeft < 10 ? new Color(0xff, 0x2b, 0x3d) : new Color(0xff, 0xea, 0x00);
            var timeLabelSize = _font.MeasureString("TIME") * scale;
            _spriteBatch.DrawString(_font, "TIME", new Vector2(ScreenW / 2f - timeLabelSize.X / 2f, pad), new Color(0xea, 0xfc, 0xff), 0, Vector2.Zero, scale, SpriteEffects.None, 0);
            _spriteBatch.DrawString(_font, timeTxt, new Vector2(ScreenW / 2f - timeSize.X / 2f, pad + fs + 2), timeColor, 0, Vector2.Zero, timeScale, SpriteEffects.None, 0);

            string speedTxt = "SPEED " + ((int)((_speed / _maxSpeed) * 260)).ToString("D3");
            var speedSize = _font.MeasureString(speedTxt) * scale;
            _spriteBatch.DrawString(_font, speedTxt, new Vector2(ScreenW - pad - speedSize.X, pad), new Color(0xff, 0x2b, 0xd6), 0, Vector2.Zero, scale, SpriteEffects.None, 0);

            var stageSize = _font.MeasureString(_hudStageText) * scale * 0.8f;
            _spriteBatch.DrawString(_font, _hudStageText, new Vector2(ScreenW - pad - stageSize.X, pad + fs + 6), new Color(0x9a, 0x8b, 0xd0), 0, Vector2.Zero, scale * 0.8f, SpriteEffects.None, 0);

            string muteTxt = _muted ? "M UNMUTE" : "M MUTE";
            var muteSize = _font.MeasureString(muteTxt) * scale * 0.75f;
            _spriteBatch.DrawString(_font, muteTxt, new Vector2(ScreenW - pad - muteSize.X, ScreenH - pad - muteSize.Y), new Color(0x3e, 0xf3, 0xff), 0, Vector2.Zero, scale * 0.75f, SpriteEffects.None, 0);

            string padTxt = _gpConnected ? "GAMEPAD: CONNECTED" : "GAMEPAD: NOT DETECTED";
            Color padColor = _gpConnected ? new Color(0x39, 0xff, 0x88) : new Color(0x9a, 0x8b, 0xd0);
            var padSize = _font.MeasureString(padTxt) * scale * 0.75f;
            _spriteBatch.DrawString(_font, padTxt, new Vector2(pad, ScreenH - pad - padSize.Y), padColor, 0, Vector2.Zero, scale * 0.75f, SpriteEffects.None, 0);
        }

        void DrawMenu()
        {
            _spriteBatch.Draw(_pixel, new Rectangle(0, 0, ScreenW, ScreenH), new Color(0xf7, 0xf6, 0xf2));

            float centerX = ScreenW / 2f;
            float y = ScreenH * 0.16f;

            if (_logo != null)
            {
                float maxW = Math.Min(ScreenW * 0.6f, 420);
                float logoScale = maxW / _logo.Width;
                if (_logo.Height * logoScale > ScreenH * 0.3f) logoScale = (ScreenH * 0.3f) / _logo.Height;
                var logoPos = new Vector2(centerX - (_logo.Width * logoScale) / 2f, y);
                _spriteBatch.Draw(_logo, logoPos, null, Color.White, 0, Vector2.Zero, logoScale, SpriteEffects.None, 0);
                y += _logo.Height * logoScale + 16;
            }

            float titleScale = MathHelper.Clamp(ScreenW * 0.045f, 26, 56) / _font.LineSpacing;
            var titleSize = _font.MeasureString(_ovTitle) * titleScale;
            _spriteBatch.DrawString(_font, _ovTitle, new Vector2(centerX - titleSize.X / 2f, y), new Color(0x0a, 0x0a, 0x0a), 0, Vector2.Zero, titleScale, SpriteEffects.None, 0);
            y += titleSize.Y + 22;

            if (_ovShowMsg)
            {
                float msgScale = MathHelper.Clamp(ScreenW * 0.014f, 12, 18) / _font.LineSpacing;
                var msgSize = _font.MeasureString(_ovMsg) * msgScale;
                _spriteBatch.DrawString(_font, _ovMsg, new Vector2(centerX - msgSize.X / 2f, y), new Color(0x0a, 0x0a, 0x0a), 0, Vector2.Zero, msgScale, SpriteEffects.None, 0);
                y += msgSize.Y + 10;
            }

            string[] lines =
            {
                "UP/W ACCELERATE    DOWN/S BRAKE",
                "LEFT/A  RIGHT/D STEER   M MUTE",
                "GAMEPAD: STICK/D-PAD STEER, A ACCEL, B BRAKE",
                "",
                "REACH THE GOAL BEFORE TIME RUNS OUT",
                "CHECKPOINTS ADD TIME - TRAFFIC SPINS YOU OUT",
                "",
                "PRESS ENTER OR START TO CHOOSE YOUR CAR"
            };
            float keyScale = MathHelper.Clamp(ScreenW * 0.011f, 9, 14) / _font.LineSpacing;
            foreach (var line in lines)
            {
                if (line.Length > 0)
                {
                    var lsz = _font.MeasureString(line) * keyScale;
                    _spriteBatch.DrawString(_font, line, new Vector2(centerX - lsz.X / 2f, y), new Color(0x55, 0x55, 0x55), 0, Vector2.Zero, keyScale, SpriteEffects.None, 0);
                }
                y += _font.LineSpacing * keyScale * 1.3f;
            }
        }

        void DrawCarSelect()
        {
            _spriteBatch.Draw(_pixel, new Rectangle(0, 0, ScreenW, ScreenH), new Color(0xf7, 0xf6, 0xf2));

            float centerX = ScreenW / 2f;
            float y = ScreenH * 0.14f;

            float titleScale = MathHelper.Clamp(ScreenW * 0.04f, 24, 50) / _font.LineSpacing;
            string title = "CHOOSE YOUR CAR";
            var titleSize = _font.MeasureString(title) * titleScale;
            _spriteBatch.DrawString(_font, title, new Vector2(centerX - titleSize.X / 2f, y), new Color(0x0a, 0x0a, 0x0a), 0, Vector2.Zero, titleScale, SpriteEffects.None, 0);
            y += titleSize.Y + 26;

            var car = PlayerCars[_carIndex];

            float swatchW = Math.Min(ScreenW * 0.32f, 320);
            float swatchH = swatchW * 0.62f;
            var swatchRect = new Rectangle((int)(centerX - swatchW / 2f), (int)y, (int)swatchW, (int)swatchH);
            _spriteBatch.Draw(_pixel, swatchRect, new Color(0x0a, 0x0a, 0x0a));
            var innerRect = new Rectangle(swatchRect.X + 6, swatchRect.Y + 6, swatchRect.Width - 12, swatchRect.Height - 12);
            _spriteBatch.Draw(_pixel, innerRect, car.Color);
            y += swatchH + 22;

            string arrowLine = (PlayerCars.Length > 1 ? "<  " : "") + car.Name + (PlayerCars.Length > 1 ? "  >" : "");
            float nameScale = MathHelper.Clamp(ScreenW * 0.026f, 16, 30) / _font.LineSpacing;
            var nameSize = _font.MeasureString(arrowLine) * nameScale;
            _spriteBatch.DrawString(_font, arrowLine, new Vector2(centerX - nameSize.X / 2f, y), new Color(0x0a, 0x0a, 0x0a), 0, Vector2.Zero, nameScale, SpriteEffects.None, 0);
            y += nameSize.Y + 8;

            string counter = (_carIndex + 1) + " / " + PlayerCars.Length;
            float counterScale = MathHelper.Clamp(ScreenW * 0.013f, 11, 16) / _font.LineSpacing;
            var counterSize = _font.MeasureString(counter) * counterScale;
            _spriteBatch.DrawString(_font, counter, new Vector2(centerX - counterSize.X / 2f, y), new Color(0x55, 0x55, 0x55), 0, Vector2.Zero, counterScale, SpriteEffects.None, 0);
            y += counterSize.Y + 30;

            string[] lines = { "LEFT/A   RIGHT/D   BROWSE", "GAMEPAD: D-PAD/STICK BROWSE, A RACE", "PRESS ENTER TO RACE" };
            float keyScale = MathHelper.Clamp(ScreenW * 0.013f, 10, 16) / _font.LineSpacing;
            foreach (var line in lines)
            {
                var lsz = _font.MeasureString(line) * keyScale;
                _spriteBatch.DrawString(_font, line, new Vector2(centerX - lsz.X / 2f, y), new Color(0x55, 0x55, 0x55), 0, Vector2.Zero, keyScale, SpriteEffects.None, 0);
                y += _font.LineSpacing * keyScale * 1.4f;
            }
        }

        void DrawResults()
        {
            _spriteBatch.Draw(_pixel, new Rectangle(0, 0, ScreenW, ScreenH), new Color(0xf7, 0xf6, 0xf2));

            float centerX = ScreenW / 2f;
            float y = ScreenH * 0.16f;

            float titleScale = MathHelper.Clamp(ScreenW * 0.06f, 34, 70) / _font.LineSpacing;
            var titleSize = _font.MeasureString(_ovTitle) * titleScale;
            _spriteBatch.DrawString(_font, _ovTitle, new Vector2(centerX - titleSize.X / 2f, y), new Color(0x0a, 0x0a, 0x0a), 0, Vector2.Zero, titleScale, SpriteEffects.None, 0);
            y += titleSize.Y + 8;

            float subScale = MathHelper.Clamp(ScreenW * 0.015f, 12, 20) / _font.LineSpacing;
            var subSize = _font.MeasureString(_ovSub) * subScale;
            _spriteBatch.DrawString(_font, _ovSub, new Vector2(centerX - subSize.X / 2f, y), new Color(0x33, 0x33, 0x33), 0, Vector2.Zero, subScale, SpriteEffects.None, 0);
            y += subSize.Y + 34;

            string scoreTxt = "SCORE  " + ((int)_score).ToString("D6");
            float scoreScale = MathHelper.Clamp(ScreenW * 0.032f, 22, 38) / _font.LineSpacing;
            var scoreSize = _font.MeasureString(scoreTxt) * scoreScale;
            _spriteBatch.DrawString(_font, scoreTxt, new Vector2(centerX - scoreSize.X / 2f, y), new Color(0x0a, 0x0a, 0x0a), 0, Vector2.Zero, scoreScale, SpriteEffects.None, 0);
            y += scoreSize.Y + 16;

            string cpTxt = "CHECKPOINTS  " + CheckpointsPassed() + " / " + _checkpoints.Count;
            float cpScale = MathHelper.Clamp(ScreenW * 0.018f, 13, 20) / _font.LineSpacing;
            var cpSize = _font.MeasureString(cpTxt) * cpScale;
            _spriteBatch.DrawString(_font, cpTxt, new Vector2(centerX - cpSize.X / 2f, y), new Color(0x33, 0x33, 0x33), 0, Vector2.Zero, cpScale, SpriteEffects.None, 0);
            y += cpSize.Y + 8;

            string timeTxt = "TIME LEFT  " + _timeLeft.ToString("0.0") + "s";
            var timeSize = _font.MeasureString(timeTxt) * cpScale;
            _spriteBatch.DrawString(_font, timeTxt, new Vector2(centerX - timeSize.X / 2f, y), new Color(0x33, 0x33, 0x33), 0, Vector2.Zero, cpScale, SpriteEffects.None, 0);
            y += timeSize.Y + 40;

            string prompt = "PRESS ENTER TO CONTINUE";
            float promptScale = MathHelper.Clamp(ScreenW * 0.014f, 12, 18) / _font.LineSpacing;
            var promptSize = _font.MeasureString(prompt) * promptScale;
            _spriteBatch.DrawString(_font, prompt, new Vector2(centerX - promptSize.X / 2f, y), new Color(0x55, 0x55, 0x55), 0, Vector2.Zero, promptScale, SpriteEffects.None, 0);
        }
    }
}
