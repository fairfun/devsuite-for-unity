using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Ff.DevSuite;
using Ff.DevSuite.Commands;
using Ff.DevSuite.Commands.Attributes;

namespace Ff.DevSuite.Samples.Asteroids
{
    [CommandCategory("Asteroids Game", Priority = 90, Description = "Controls and debug commands for the Asteroids sample game.")]
    public class AsteroidsGame : MonoBehaviour
    {
        public static AsteroidsGame Instance { get; private set; }

        [Header("Ship Settings")][CommandValue][SerializeField] private float _shipSpeed = 12f;
        [CommandValue][SerializeField] private float _rotationSpeed = 220f;
        [SerializeField] private float _drag = 1.2f;
        [CommandValue][SerializeField] private float _bulletSpeed = 22f;
        [CommandValue][SerializeField] private float _fireRate = 6f;

        [Header("Asteroid Settings")][SerializeField] private float _asteroidSpeedMultiplier = 1.2f;
        [SerializeField] private int _initialAsteroidCount = 4;

        [Header("Game State")][CommandValue][SerializeField] private int _lives = 3;
        [CommandValue(ReadOnly = true)][SerializeField] private int _score = 0;
        [CommandValue][SerializeField] private bool _godMode = false;

        [CommandValue] public float AsteroidSpeed { get => _asteroidSpeedMultiplier; set => _asteroidSpeedMultiplier = value; }
        [CommandValue] public int ActiveAsteroidsCount => _asteroids.Count;
        [CommandValue] public int ActiveBulletsCount => _bullets.Count;

        [CommandButton(Title = "Spawn Wave", Color = "#66CCFF", Description = "Spawn a new wave of large asteroids around the screen edges.")]
        private void SpawnWave(int count = 4)
        {
            for (var i = 0; i < count; i++)
            {
                SpawnAsteroid(AsteroidSize.Large);
            }
        }

        [CommandButton(Title = "Spawn 1 Asteroid", Description = "Spawn a single large asteroid.")]
        public void SpawnOneAsteroid()
        {
            SpawnAsteroid(AsteroidSize.Large);
        }

        [CommandButton(Title = "Nuke Asteroids", Color = "#FF5555", Description = "Destroys all active asteroids on screen with score and debris.")]
        public void NukeAllAsteroids()
        {
            foreach (var asteroid in _asteroids.ToArray())
            {
                DestroyAsteroid(asteroid, false, true);
            }
        }

        [CommandButton(Title = "Clear Asteroids", Description = "Removes all asteroids without awarding points.")]
        private void ClearAsteroids()
        {
            foreach (var asteroid in _asteroids.ToArray())
            {
                Destroy(asteroid.gameObject);
            }
            _asteroids.Clear();
        }

        [CommandButton(Title = "Destroy Asteroid", Color = "#FF5555", Description = "Destroys the selected asteroid from the dropdown.")]
        public void DestroySelectedAsteroid(AsteroidEntity asteroid = null)
        {
            if (asteroid == null && _asteroids.Count > 0)
            {
                asteroid = _asteroids[0];
            }
            if (asteroid != null && _asteroids.Contains(asteroid))
            {
                DestroyAsteroid(asteroid, false, true);
            }
        }

        [CommandButton(Title = "Reset Game", Color = "#FFCC00", Description = "Reset score, restore lives, and start a fresh wave.")]
        private void ResetGame()
        {
            _score = 0;
            _lives = 3;
            _asteroidCounter = 0;
            _isGameOver = false;
            ClearAsteroids();
            ClearBullets();
            ClearDebris();

            if (_ship != null)
            {
                _ship.ResetPosition();
            }

            SpawnWave(_initialAsteroidCount);
            UpdateUI();
        }

        [CommandButton(Title = "+500 Score", Description = "Add bonus points to score.")]
        public void AddScore(int amount = 500)
        {
            _score += amount;
        }

        [CommandButton(Title = "+1 Life", Description = "Add an extra life.")]
        public void AddLife()
        {
            _lives++;
        }

        private Camera _mainCamera;
        private PlayerShip _ship;
        private readonly List<AsteroidEntity> _asteroids = new();
        private readonly List<BulletEntity> _bullets = new();
        private readonly List<DebrisLine> _debris = new();

        private float _screenHalfWidth;
        private float _screenHalfHeight;
        private float _lastFireTime;
        private bool _isGameOver;
        private Material _lineMaterial;
        private int _asteroidCounter;
        private AsteroidEntityAdapter _asteroidAdapter;
        private CommandValuesProvider _asteroidValuesProvider;

        [Header("UI References")]
        [SerializeField] private Canvas _canvas;
        [SerializeField] private Text _hudText;
        [SerializeField] private Text _gameOverText;
        [SerializeField] private RectTransform _devSuiteArrow;

        private bool _devSuiteOpenedInSession;
        private Vector2 _arrowBasePos = new Vector2(-120f, -120f);

        public Material LineMaterial
        {
            get
            {
                if (_lineMaterial == null)
                {
                    var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                    _lineMaterial = new Material(shader)
                    {
                        hideFlags = HideFlags.DontSave,
                    };
                }
                return _lineMaterial;
            }
        }

        public AsteroidEntity FindAsteroidByName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            for (var i = 0; i < _asteroids.Count; i++)
            {
                var a = _asteroids[i];
                if (a != null && a.name == name)
                {
                    return a;
                }
            }
            return null;
        }

        private void Awake()
        {
            Instance = this;
            _mainCamera = Camera.main;
            UpdateScreenBounds();
            if (_hudText == null)
            {
                CreateUI();
            }
            else
            {
                EnsureFont(_hudText);
                EnsureFont(_gameOverText);
                if (_devSuiteArrow != null)
                {
                    _arrowBasePos = _devSuiteArrow.anchoredPosition;
                }
            }
        }

        private void EnsureFont(Text text)
        {
            if (text != null && text.font == null)
            {
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                    ?? Resources.GetBuiltinResource<Font>("Arial.ttf")
                    ?? Font.CreateDynamicFontFromOSFont("Arial", 16);
            }
        }

        private void Start()
        {
            _asteroidAdapter = new AsteroidEntityAdapter();
            _asteroidValuesProvider = new CommandValuesProvider(typeof(AsteroidEntity), _ => _asteroids.ToArray());
            DevSuiteContext.Default.CommandsApi?.RegisterAdapter(_asteroidAdapter);
            DevSuiteContext.Default.CommandsApi?.RegisterValuesProvider(_asteroidValuesProvider);

            DevSuiteContext.Default.AttributesParser?.RegisterInstance(this);

            CreateShip();
            ResetGame();
        }

        private void OnDestroy()
        {
            DevSuiteContext.Default.AttributesParser?.UnregisterInstance(this);
            if (_asteroidAdapter != null)
            {
                DevSuiteContext.Default.CommandsApi?.UnregisterAdapter(_asteroidAdapter);
            }
            if (_asteroidValuesProvider != null)
            {
                DevSuiteContext.Default.CommandsApi?.UnregisterValuesProvider(_asteroidValuesProvider);
            }

            ClearDebris();

            if (_lineMaterial != null)
            {
                DestroyImmediate(_lineMaterial);
            }
        }

        private void Update()
        {
            UpdateScreenBounds();
            UpdateUI();
            UpdateDebris();

            if (_isGameOver)
            {
                if (Input.GetKeyDown(KeyCode.R))
                {
                    ResetGame();
                }
                return;
            }

            HandleInput();
            CheckCollisions();

            if (_asteroids.Count == 0 && !_isGameOver)
            {
                _initialAsteroidCount++;
                SpawnWave(_initialAsteroidCount);
            }
        }

        private void UpdateScreenBounds()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
                if (_mainCamera == null)
                {
                    return;
                }
            }

            _screenHalfHeight = _mainCamera.orthographicSize;
            _screenHalfWidth = _screenHalfHeight * _mainCamera.aspect;
        }

        public Vector3 WrapPosition(Vector3 position, float margin = 0.5f)
        {
            if (position.x > _screenHalfWidth + margin)
            {
                position.x = -_screenHalfWidth - margin;
            }
            else if (position.x < -_screenHalfWidth - margin)
            {
                position.x = _screenHalfWidth + margin;
            }

            if (position.y > _screenHalfHeight + margin)
            {
                position.y = -_screenHalfHeight - margin;
            }
            else if (position.y < -_screenHalfHeight - margin)
            {
                position.y = _screenHalfHeight + margin;
            }

            return position;
        }

        private void CreateShip()
        {
            var shipObj = new GameObject("PlayerShip");
            shipObj.transform.SetParent(transform);
            _ship = shipObj.AddComponent<PlayerShip>();
            _ship.Init(this);
        }

        private void HandleInput()
        {
            if (_ship == null || !_ship.IsAlive)
            {
                return;
            }

            var horizontal = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
            {
                horizontal += 1f;
            }
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
            {
                horizontal -= 1f;
            }

            var thrust = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);

            _ship.HandleMovement(horizontal, thrust, _rotationSpeed, _shipSpeed, _drag);

            if ((Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0)) && Time.time >= _lastFireTime + (1f / _fireRate))
            {
                _lastFireTime = Time.time;
                FireBullet(_ship.transform.position + (_ship.transform.up * 0.4f), _ship.transform.up);
            }
        }

        private void FireBullet(Vector3 position, Vector3 direction)
        {
            var bulletObj = new GameObject($"Bullet_{_bullets.Count}");
            bulletObj.transform.SetParent(transform);
            var bullet = bulletObj.AddComponent<BulletEntity>();
            bullet.Init(this, position, direction * _bulletSpeed);
            _bullets.Add(bullet);
        }

        public void RemoveBullet(BulletEntity bullet)
        {
            _bullets.Remove(bullet);
        }

        private void ClearBullets()
        {
            var list = new List<BulletEntity>(_bullets);
            foreach (var b in list)
            {
                if (b != null)
                {
                    Destroy(b.gameObject);
                }
            }
            _bullets.Clear();
        }

        private void SpawnAsteroid(AsteroidSize size, Vector3? spawnPosition = null)
        {
            Vector3 pos;
            if (spawnPosition.HasValue)
            {
                pos = spawnPosition.Value;
            }
            else
            {
                var edgeHorizontal = UnityEngine.Random.value > 0.5f;
                if (edgeHorizontal)
                {
                    var x = UnityEngine.Random.value > 0.5f ? _screenHalfWidth + 0.8f : -_screenHalfWidth - 0.8f;
                    var y = UnityEngine.Random.Range(-_screenHalfHeight, _screenHalfHeight);
                    pos = new Vector3(x, y, 0);
                }
                else
                {
                    var x = UnityEngine.Random.Range(-_screenHalfWidth, _screenHalfWidth);
                    var y = UnityEngine.Random.value > 0.5f ? _screenHalfHeight + 0.8f : -_screenHalfHeight - 0.8f;
                    pos = new Vector3(x, y, 0);
                }
            }

            var astObj = new GameObject($"Asteroid #{++_asteroidCounter} ({size})");
            astObj.transform.SetParent(transform);
            var asteroid = astObj.AddComponent<AsteroidEntity>();

            var randomDir = UnityEngine.Random.insideUnitCircle.normalized;
            var baseSpeed = size switch
            {
                AsteroidSize.Large => UnityEngine.Random.Range(1.2f, 2.0f),
                AsteroidSize.Medium => UnityEngine.Random.Range(2.0f, 3.2f),
                _ => UnityEngine.Random.Range(3.0f, 4.5f),
            };

            asteroid.Init(this, size, pos, randomDir * (baseSpeed * _asteroidSpeedMultiplier));
            _asteroids.Add(asteroid);
        }

        private void DestroyAsteroid(AsteroidEntity asteroid, bool spawnSplits, bool awardScore)
        {
            if (asteroid == null)
            {
                return;
            }

            var pos = asteroid.transform.position;
            var size = asteroid.Size;

            if (awardScore)
            {
                _score += size switch
                {
                    AsteroidSize.Large => 20,
                    AsteroidSize.Medium => 50,
                    _ => 100,
                };
            }

            SpawnDebris(
                pos,
                size switch
                {
                    AsteroidSize.Large => 8,
                    AsteroidSize.Medium => 6,
                    _ => 4,
                }
            );

            _asteroids.Remove(asteroid);
            Destroy(asteroid.gameObject);

            if (spawnSplits)
            {
                if (size == AsteroidSize.Large)
                {
                    SpawnAsteroid(AsteroidSize.Medium, pos);
                    SpawnAsteroid(AsteroidSize.Medium, pos);
                }
                else if (size == AsteroidSize.Medium)
                {
                    SpawnAsteroid(AsteroidSize.Small, pos);
                    SpawnAsteroid(AsteroidSize.Small, pos);
                }
            }
        }

        private void CheckCollisions()
        {
            for (var b = _bullets.Count - 1; b >= 0; b--)
            {
                var bullet = _bullets[b];
                if (bullet == null)
                {
                    continue;
                }

                for (var a = _asteroids.Count - 1; a >= 0; a--)
                {
                    var asteroid = _asteroids[a];
                    if (asteroid == null)
                    {
                        continue;
                    }

                    var distSqr = (bullet.transform.position - asteroid.transform.position).sqrMagnitude;
                    var radius = asteroid.Radius;

                    if (distSqr <= radius * radius)
                    {
                        Destroy(bullet.gameObject);
                        _bullets.RemoveAt(b);
                        DestroyAsteroid(asteroid, true, true);
                        break;
                    }
                }
            }

            if (_ship != null && _ship.IsAlive && !_godMode && !_ship.IsInvulnerable)
            {
                for (var a = _asteroids.Count - 1; a >= 0; a--)
                {
                    var asteroid = _asteroids[a];
                    if (asteroid == null)
                    {
                        continue;
                    }

                    var distSqr = (_ship.transform.position - asteroid.transform.position).sqrMagnitude;
                    var combinedRadius = 0.35f + asteroid.Radius;

                    if (distSqr <= combinedRadius * combinedRadius)
                    {
                        OnShipHit();
                        DestroyAsteroid(asteroid, true, false);
                        break;
                    }
                }
            }
        }

        private void OnShipHit()
        {
            SpawnDebris(_ship.transform.position, 12);
            _lives--;

            if (_lives <= 0)
            {
                _isGameOver = true;
                _ship.Die();
            }
            else
            {
                _ship.Respawn();
            }
        }

        private void SpawnDebris(Vector3 center, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var dir = UnityEngine.Random.insideUnitCircle.normalized;
                var speed = UnityEngine.Random.Range(2f, 6f);
                var length = UnityEngine.Random.Range(0.2f, 0.5f);

                var debrisObj = new GameObject("Debris");
                debrisObj.transform.SetParent(transform);
                var lr = debrisObj.AddComponent<LineRenderer>();
                lr.material = LineMaterial;
                lr.useWorldSpace = true;
                lr.startWidth = 0.04f;
                lr.endWidth = 0.02f;
                lr.positionCount = 2;

                var deb = new DebrisLine
                {
                    GameObject = debrisObj,
                    Renderer = lr,
                    Position = center,
                    Velocity = dir * speed,
                    Direction = dir * length,
                    Lifetime = UnityEngine.Random.Range(0.4f, 0.8f),
                    Age = 0f,
                };
                _debris.Add(deb);
            }
        }

        private void UpdateDebris()
        {
            for (var i = _debris.Count - 1; i >= 0; i--)
            {
                var deb = _debris[i];
                deb.Age += Time.deltaTime;

                if (deb.Age >= deb.Lifetime || deb.GameObject == null)
                {
                    if (deb.GameObject != null)
                    {
                        Destroy(deb.GameObject);
                    }
                    _debris.RemoveAt(i);
                    continue;
                }

                deb.Position += deb.Velocity * Time.deltaTime;
                var alpha = Mathf.Clamp01(1f - (deb.Age / deb.Lifetime));
                var col = new Color(1f, 1f, 1f, alpha);
                deb.Renderer.startColor = col;
                deb.Renderer.endColor = col;
                deb.Renderer.SetPosition(0, deb.Position);
                deb.Renderer.SetPosition(1, deb.Position + deb.Direction);
            }
        }

        private void ClearDebris()
        {
            for (var i = _debris.Count - 1; i >= 0; i--)
            {
                if (_debris[i]?.GameObject != null)
                {
                    Destroy(_debris[i].GameObject);
                }
            }
            _debris.Clear();
        }

        private void CreateUI()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                ?? Resources.GetBuiltinResource<Font>("Arial.ttf")
                ?? Font.CreateDynamicFontFromOSFont("Arial", 16);

            var canvasGo = new GameObject("HUDCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 0;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            var hudGo = new GameObject("HUDText");
            hudGo.transform.SetParent(canvasGo.transform, false);
            var hudRect = hudGo.AddComponent<RectTransform>();
            hudRect.anchorMin = new Vector2(0f, 1f);
            hudRect.anchorMax = new Vector2(0f, 1f);
            hudRect.pivot = new Vector2(0f, 1f);
            hudRect.anchoredPosition = new Vector2(30f, -30f);
            hudRect.sizeDelta = new Vector2(800f, 400f);

            _hudText = hudGo.AddComponent<Text>();
            _hudText.font = font;
            _hudText.fontSize = 20;
            _hudText.lineSpacing = 1.2f;
            _hudText.color = Color.white;
            _hudText.alignment = TextAnchor.UpperLeft;
            _hudText.raycastTarget = false;

            var hudShadow = hudGo.AddComponent<Shadow>();
            hudShadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            hudShadow.effectDistance = new Vector2(1f, -1f);

            var gameOverGo = new GameObject("GameOverText");
            gameOverGo.transform.SetParent(canvasGo.transform, false);
            var gameOverRect = gameOverGo.AddComponent<RectTransform>();
            gameOverRect.anchorMin = new Vector2(0.5f, 0.5f);
            gameOverRect.anchorMax = new Vector2(0.5f, 0.5f);
            gameOverRect.pivot = new Vector2(0.5f, 0.5f);
            gameOverRect.anchoredPosition = Vector2.zero;
            gameOverRect.sizeDelta = new Vector2(800f, 200f);

            _gameOverText = gameOverGo.AddComponent<Text>();
            _gameOverText.font = font;
            _gameOverText.fontSize = 40;
            _gameOverText.fontStyle = FontStyle.Bold;
            _gameOverText.color = new Color(1f, 0.3f, 0.3f, 1f);
            _gameOverText.alignment = TextAnchor.MiddleCenter;
            _gameOverText.text = "GAME OVER\nPress R to Restart";
            _gameOverText.raycastTarget = false;

            var goShadow = gameOverGo.AddComponent<Shadow>();
            goShadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            goShadow.effectDistance = new Vector2(2f, -2f);

            gameOverGo.SetActive(false);

            var arrowGo = new GameObject("DevSuiteArrow");
            arrowGo.transform.SetParent(canvasGo.transform, false);
            _devSuiteArrow = arrowGo.AddComponent<RectTransform>();
            _devSuiteArrow.anchorMin = Vector2.one;
            _devSuiteArrow.anchorMax = Vector2.one;
            _devSuiteArrow.pivot = new Vector2(0.5f, 0.5f);
            _devSuiteArrow.anchoredPosition = _arrowBasePos = new Vector2(-120f, -120f);
            _devSuiteArrow.sizeDelta = new Vector2(110f, 44f);
            _devSuiteArrow.localEulerAngles = new Vector3(0, 0, 45f);

            var img = arrowGo.AddComponent<Image>();
            img.raycastTarget = false;
            img.sprite = Resources.Load<Sprite>("DevSuiteArrow");

            var arrowShadow = arrowGo.AddComponent<Shadow>();
            arrowShadow.effectColor = new Color(0f, 0f, 0f, 0.75f);
            arrowShadow.effectDistance = new Vector2(2f, -2f);
        }

        private void UpdateUI()
        {
            UpdateDevSuiteArrow();

            if (_hudText != null)
            {
                var godModeStr = _godMode ? " (GOD MODE)" : "";
                _hudText.text = $"SCORE: {_score}\nLIVES: {_lives}{godModeStr}\nASTEROIDS: {_asteroids.Count}\n\n" +
                    "Fly: [W / Up] Thrust, [A/D / Left/Right] Rotate\n" +
                    "Fire: [Space] or Left Click | Restart: [R]\n" +
                    "DevSuite: Press Ctrl + ` or toggle top-right panel";
            }

            if (_gameOverText != null)
            {
                _gameOverText.gameObject.SetActive(_isGameOver);
            }
        }

        private void UpdateDevSuiteArrow()
        {
            if (_devSuiteArrow == null || _devSuiteOpenedInSession)
            {
                return;
            }

            if (DevSuiteContext.Default.PanelExpanded)
            {
                _devSuiteOpenedInSession = true;
                _devSuiteArrow.gameObject.SetActive(false);
                return;
            }

            var bounce = Mathf.PingPong(Time.unscaledTime * 120f, 60f);
            _devSuiteArrow.anchoredPosition = _arrowBasePos + new Vector2(1f, 1f).normalized * bounce;
        }

        private class DebrisLine
        {
            public GameObject GameObject;
            public LineRenderer Renderer;
            public Vector3 Position;
            public Vector3 Velocity;
            public Vector3 Direction;
            public float Lifetime;
            public float Age;
        }
    }

    public enum AsteroidSize
    {
        Large,
        Medium,
        Small,
    }

    public class AsteroidEntity : MonoBehaviour
    {
        [SerializeField] private AsteroidSize _size = AsteroidSize.Large;
        [SerializeField] private float _radius = 1.2f;
        [SerializeField] private Vector3 _velocity;
        [SerializeField] private float _rotationSpeed;

        private AsteroidsGame _game;
        private LineRenderer _lineRenderer;

        public AsteroidSize Size => _size;
        public float Radius => _radius;
        public Vector3 Velocity => _velocity;

        public void Init(AsteroidsGame game, AsteroidSize size, Vector3 position, Vector3 velocity)
        {
            _game = game;
            _size = size;
            _velocity = velocity;
            _rotationSpeed = UnityEngine.Random.Range(-90f, 90f);
            transform.position = position;

            _radius = size switch
            {
                AsteroidSize.Large => 1.2f,
                AsteroidSize.Medium => 0.65f,
                _ => 0.35f,
            };

            var col = gameObject.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = _radius;

            var pointCount = size switch
            {
                AsteroidSize.Large => 12,
                AsteroidSize.Medium => 10,
                _ => 8,
            };

            _lineRenderer = gameObject.AddComponent<LineRenderer>();
            _lineRenderer.material = _game.LineMaterial;
            _lineRenderer.useWorldSpace = false;
            _lineRenderer.loop = true;
            _lineRenderer.startWidth = 0.04f;
            _lineRenderer.endWidth = 0.04f;

            var asteroidColor = new Color(0.85f, 0.85f, 0.9f, 1f);
            _lineRenderer.startColor = asteroidColor;
            _lineRenderer.endColor = asteroidColor;

            var points = new Vector3[pointCount];
            var angleStep = 360f / pointCount;

            for (var i = 0; i < pointCount; i++)
            {
                var angle = i * angleStep * Mathf.Deg2Rad;
                var r = _radius * UnityEngine.Random.Range(0.75f, 1.25f);
                points[i] = new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, 0);
            }

            _lineRenderer.positionCount = pointCount;
            _lineRenderer.SetPositions(points);
        }

        private void Update()
        {
            transform.position += _velocity * Time.deltaTime;
            transform.Rotate(0, 0, _rotationSpeed * Time.deltaTime);

            transform.position = _game.WrapPosition(transform.position, _radius);
        }
    }

    public class BulletEntity : MonoBehaviour
    {
        [SerializeField] private Vector3 _velocity;
        [SerializeField] private float _lifetime = 1.6f;

        private AsteroidsGame _game;
        private LineRenderer _lineRenderer;
        private float _age;

        public void Init(AsteroidsGame game, Vector3 position, Vector3 velocity)
        {
            _game = game;
            _velocity = velocity;
            transform.position = position;

            var col = gameObject.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.2f;

            _lineRenderer = gameObject.AddComponent<LineRenderer>();
            _lineRenderer.material = _game.LineMaterial;
            _lineRenderer.useWorldSpace = true;
            _lineRenderer.startWidth = 0.06f;
            _lineRenderer.endWidth = 0.03f;

            var bulletColor = new Color(1f, 0.95f, 0.4f, 1f);
            _lineRenderer.startColor = bulletColor;
            _lineRenderer.endColor = bulletColor;

            _lineRenderer.positionCount = 2;
            UpdateLine();
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age >= _lifetime)
            {
                _game.RemoveBullet(this);
                Destroy(gameObject);
                return;
            }

            transform.position += _velocity * Time.deltaTime;
            transform.position = _game.WrapPosition(transform.position, 0.2f);
            UpdateLine();
        }

        private void UpdateLine()
        {
            var trail = -_velocity.normalized * 0.25f;
            _lineRenderer.SetPosition(0, transform.position);
            _lineRenderer.SetPosition(1, transform.position + trail);
        }

        private void OnDestroy()
        {
            if (_game != null)
            {
                _game.RemoveBullet(this);
            }
        }
    }

    public class PlayerShip : MonoBehaviour
    {
        [SerializeField] private Vector2 _velocity;
        [SerializeField] private bool _isAlive = true;
        [SerializeField] private bool _isInvulnerable = false;

        private AsteroidsGame _game;
        private LineRenderer _hullRenderer;
        private LineRenderer _thrustRenderer;
        private CircleCollider2D _collider;
        private float _invulnerableTimer;

        public bool IsAlive => _isAlive;
        public bool IsInvulnerable => _isInvulnerable;
        public Vector2 Velocity => _velocity;

        public void Init(AsteroidsGame game)
        {
            _game = game;

            _collider = gameObject.AddComponent<CircleCollider2D>();
            _collider.isTrigger = true;
            _collider.radius = 0.45f;

            _hullRenderer = gameObject.AddComponent<LineRenderer>();
            _hullRenderer.material = _game.LineMaterial;
            _hullRenderer.useWorldSpace = false;
            _hullRenderer.loop = true;
            _hullRenderer.startWidth = 0.05f;
            _hullRenderer.endWidth = 0.05f;

            var shipColor = new Color(0.3f, 0.9f, 1f, 1f);
            _hullRenderer.startColor = shipColor;
            _hullRenderer.endColor = shipColor;

            var hullPoints = new Vector3[]
            {
                new(0f, 0.45f, 0f), new(-0.3f, -0.35f, 0f), new(-0.15f, -0.2f, 0f), new(0.15f, -0.2f, 0f), new(0.3f, -0.35f, 0f),
            };
            _hullRenderer.positionCount = hullPoints.Length;
            _hullRenderer.SetPositions(hullPoints);

            var thrustObj = new GameObject("ThrusterFlame");
            thrustObj.transform.SetParent(transform);
            thrustObj.transform.localPosition = Vector3.zero;
            thrustObj.transform.localRotation = Quaternion.identity;

            _thrustRenderer = thrustObj.AddComponent<LineRenderer>();
            _thrustRenderer.material = _game.LineMaterial;
            _thrustRenderer.useWorldSpace = false;
            _thrustRenderer.loop = false;
            _thrustRenderer.startWidth = 0.04f;
            _thrustRenderer.endWidth = 0.02f;

            var thrustColor = new Color(1f, 0.5f, 0.1f, 1f);
            _thrustRenderer.startColor = thrustColor;
            _thrustRenderer.endColor = thrustColor;

            var thrustPoints = new Vector3[]
            {
                new(-0.1f, -0.22f, 0f), new(0f, -0.45f, 0f), new(0.1f, -0.22f, 0f),
            };
            _thrustRenderer.positionCount = thrustPoints.Length;
            _thrustRenderer.SetPositions(thrustPoints);
            _thrustRenderer.enabled = false;
        }

        public void HandleMovement(float rotateInput, bool thrustInput, float rotationSpeed, float thrustPower, float drag)
        {
            transform.Rotate(0, 0, rotateInput * rotationSpeed * Time.deltaTime);

            if (thrustInput)
            {
                _velocity += (Vector2)(transform.up * (thrustPower * Time.deltaTime));
                _thrustRenderer.enabled = UnityEngine.Random.value > 0.2f;
            }
            else
            {
                _thrustRenderer.enabled = false;
            }

            _velocity *= Mathf.Clamp01(1f - (drag * Time.deltaTime));
            transform.position += (Vector3)(_velocity * Time.deltaTime);

            transform.position = _game.WrapPosition(transform.position, 0.5f);

            if (_isInvulnerable)
            {
                _invulnerableTimer -= Time.deltaTime;
                _hullRenderer.enabled = Mathf.Repeat(Time.time * 10f, 1f) > 0.4f;

                if (_invulnerableTimer <= 0)
                {
                    _isInvulnerable = false;
                    _hullRenderer.enabled = true;
                }
            }
        }

        public void ResetPosition()
        {
            transform.position = Vector3.zero;
            transform.rotation = Quaternion.identity;
            _velocity = Vector2.zero;
            _isAlive = true;
            _isInvulnerable = true;
            _invulnerableTimer = 2.5f;
            if (_collider != null)
            {
                _collider.enabled = true;
            }
            _hullRenderer.enabled = true;
            _thrustRenderer.enabled = false;
        }

        public void Respawn()
        {
            ResetPosition();
        }

        public void Die()
        {
            _isAlive = false;
            if (_collider != null)
            {
                _collider.enabled = false;
            }
            _hullRenderer.enabled = false;
            _thrustRenderer.enabled = false;
            _velocity = Vector2.zero;
        }
    }

    public class AsteroidEntityAdapter : CommandValueAdapter
    {
        private static readonly List<Type> _destinationsFromString = new() { typeof(AsteroidEntity) };
        private static readonly List<Type> _destinationsToString = new() { typeof(string) };
        private static readonly List<Type> _empty = new();

        public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
        {
            if (typeSource == typeof(string))
            {
                return _destinationsFromString;
            }
            if (typeof(AsteroidEntity).IsAssignableFrom(typeSource))
            {
                return _destinationsToString;
            }
            return _empty;
        }

        public override object Convert(object objSource, Type typeDestination, object objDestination)
        {
            if (typeDestination == typeof(string))
            {
                if (objSource is AsteroidEntity asteroid && asteroid != null)
                {
                    return asteroid.name;
                }
                return null;
            }

            if (typeDestination == typeof(AsteroidEntity))
            {
                if (objSource is AsteroidEntity ast)
                {
                    return ast;
                }
                if (objSource is string str && AsteroidsGame.Instance != null)
                {
                    return AsteroidsGame.Instance.FindAsteroidByName(str);
                }
            }

            return null;
        }
    }
}