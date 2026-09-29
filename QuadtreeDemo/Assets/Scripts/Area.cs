using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;
using System.Collections;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;


public class Area : MonoBehaviour
{
    enum CollisionMode { BruteForce, Quadtree }

    [SerializeField] Rect bounds = new Rect(-10f, -10f, 20f, 20f);
    [SerializeField] Color lineColor = Color.white;

    [Header("Balls")]
    [SerializeField] int ballCount = 500;
    [SerializeField] float ballRadius = 0.15f;
    [SerializeField] float minSpeed = 1f;
    [SerializeField] float maxSpeed = 3f;
    [SerializeField] Color ballColor = Color.cyan;
    [SerializeField] Color collidingColor = Color.red;

    [Header("Quadtree")]
    [SerializeField] int nodeCapacity = 4;
    [SerializeField] int maxDepth = 8;
    [SerializeField] bool showTree = true;
    [SerializeField] Color nodeColor = new Color(1f, 1f, 1f, 0.25f);

    [Header("Demo")]
    [SerializeField] CollisionMode mode = CollisionMode.Quadtree;
    [SerializeField] int ballCountStep = 500;

    [Header("Benchmark")]
    [SerializeField] int[] benchmarkCounts = { 500, 1000, 2000, 4000 };
    [SerializeField] int warmupFrames = 10;
    [SerializeField] int sampleFrames = 120;

    private const int CircleSegments = 12;

    private readonly List<Ball> balls = new List<Ball>();

    Quadtree tree;
    private readonly List<Rect> nodeBounds = new List<Rect>();
    private readonly List<Ball> queryResult = new List<Ball>();

    private Material lineMaterial;

    // 측정용
    private readonly Stopwatch stopwatch = new Stopwatch();
    private int checkCount;
    private float collisionMs;
    private float smoothdDeltaTime;
    private GUIStyle labelStyle;
    private bool isBenchmarking;
    private float measuredMs;
    private float measuredChecks;

    private void Awake()
    {
        // Unity 내장 셰이더로 정점 색을 그대로 출력(반투명 선으로 출력)
        lineMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
        lineMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        lineMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        lineMaterial.SetInt("_Cull", (int)CullMode.Off);
        lineMaterial.SetInt("_ZWrite", 0);
    }

    private void OnEnable()
    {
        RenderPipelineManager.endCameraRendering += RenderPipelineManager_endCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= RenderPipelineManager_endCameraRendering;
    }

    private void Start()
    {
        SpawnBalls();
    }

    private void Update()
    {
        HandleInput();

        float dt = Time.deltaTime;
        smoothdDeltaTime = Mathf.Lerp(smoothdDeltaTime, Time.unscaledDeltaTime, 0.1f);

        foreach(Ball ball in balls)
        {
            ball.Position += ball.Velocity * dt;
            BounceOffWalls(ball);
        }

        // 충돌 처리 구간만 시간 측정
        stopwatch.Restart();
        checkCount = 0;

        if (mode == CollisionMode.Quadtree)
        {

            BuildTree();
            DetectCollisionsQuadtree();
        }
        else
        {
            tree = null;
            DetectCollisionsBruteForce();
        }

        stopwatch.Stop();
        collisionMs = (float)stopwatch.Elapsed.TotalMilliseconds;

    }

    private void HandleInput()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;
        if (isBenchmarking) return;

        if(kb.bKey.wasPressedThisFrame)
        {
            StartCoroutine(RunBenchmark());
            return;
        }

        if(kb.spaceKey.wasPressedThisFrame)
        {
            mode = mode == CollisionMode.Quadtree ? CollisionMode.BruteForce : CollisionMode.Quadtree;
        }

        if(kb.tKey.wasPressedThisFrame)
        {
            showTree = !showTree;
        }

        if(kb.upArrowKey.wasPressedThisFrame)
        {
            ballCount += ballCountStep;
            SpawnBalls();
        }

        if(kb.downArrowKey.wasPressedThisFrame)
        {
            ballCount = Mathf.Max(ballCountStep, ballCount - ballCountStep);
            SpawnBalls();
        }

        if(kb.rKey.wasPressedThisFrame)
        {
            SpawnBalls();
        }
    }

    IEnumerator RunBenchmark()
    { 
        isBenchmarking = true;
        CollisionMode originalMode = mode;
        int originalCount = ballCount;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("| Balls |  Brute ms | Brute Checks | Quadtree ms | Quadtree Checks |");
        sb.AppendLine("|---|---|---|---|---|");

        foreach(int count in benchmarkCounts)
        {
            ballCount = count;
            SpawnBalls();

            yield return Measure(CollisionMode.BruteForce);
            float bruteMs = measuredMs;
            float bruteChecks = measuredChecks;

            yield return Measure(CollisionMode.Quadtree);
            float quadMs = measuredMs;
            float quadChecks = measuredChecks;

            sb.AppendLine($"| {count} | {bruteMs:F2} | {bruteChecks:N0} | {quadMs:F2} | {quadChecks:N0}");
        }

        string table = sb.ToString();
        UnityEngine.Debug.Log(table);
        GUIUtility.systemCopyBuffer = table;

        mode = originalMode;
        ballCount = originalCount;
        SpawnBalls();
        isBenchmarking = false;
    }

    IEnumerator Measure(CollisionMode targetMode)
    {
        mode = targetMode;

        for(int i = 0; i < warmupFrames; i++)
        {
            yield return null;
        }

        float totalMs = 0f;
        long totalChecks = 0;

        for(int i = 0; i < sampleFrames; i++)
        {
            yield return null;
            totalMs += collisionMs;
            totalChecks += checkCount;
        }

        measuredMs = totalMs / sampleFrames;
        measuredChecks = (float)totalChecks / sampleFrames;
    }
    private void BuildTree()
    {
        tree = new Quadtree(bounds, nodeCapacity, maxDepth);
        foreach (Ball ball in balls)
        {
            tree.Insert(ball);
        }
    }

    private void DetectCollisionsQuadtree()
    {
        foreach (Ball ball in balls)
        {
            ball.IsColliding = false;
        }

        foreach (Ball ball in balls)
        {
            // 이 공과 부딪힐 수 있는 공의 "중심"이 있을 수 있는 범위
            float reach = ball.Radius * 2f;
            Rect range = new Rect(
                ball.Position.x - reach,
                ball.Position.y - reach,
                reach * 2f,
                reach * 2f);

            queryResult.Clear();
            tree.Query(range, queryResult);

            foreach (Ball other in queryResult)
            {
                if (other == ball) continue;

                checkCount++;
                if(IsOverlapping(ball, other))
                {
                    ball.IsColliding = true;
                    break;
                }
            }
        }
    }

    private void DetectCollisionsBruteForce()
    {
        foreach(Ball ball in balls)
        {
            ball.IsColliding = false;

            foreach(Ball other in balls)
            {
                if (other == ball) continue;

                checkCount++;
                if(IsOverlapping(ball, other))
                {
                    ball.IsColliding = true;
                    break;
                }
            }
        }
    }

    static bool IsOverlapping(Ball a, Ball b)
    {
        float minDist = a.Radius + b.Radius;
        return (a.Position - b.Position).sqrMagnitude < minDist * minDist;
    }

    private void SpawnBalls()
    {
        balls.Clear();

        for(int i = 0; i < ballCount; i++)
        {
            Ball ball = new Ball();
            ball.Radius = ballRadius;
            ball.Position = new Vector2(
                Random.Range(bounds.xMin + ballRadius, bounds.xMax - ballRadius),
                Random.Range(bounds.yMin + ballRadius, bounds.yMax - ballRadius)
            );

            float angle = Random.Range(0f, Mathf.PI * 2f);
            float speed = Random.Range(minSpeed, maxSpeed); 
            ball.Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;

            balls.Add(ball);
        }
    }


    private void BounceOffWalls(Ball ball)
    {
        if (ball.Position.x - ball.Radius < bounds.xMin)
        {
            ball.Position.x = bounds.xMin + ball.Radius;
            ball.Velocity.x = Mathf.Abs(ball.Velocity.x);
        }
        else if (ball.Position.x + ball.Radius > bounds.xMax)
        {
            ball.Position.x = bounds.xMax - ball.Radius;
            ball.Velocity.x = -Mathf.Abs(ball.Velocity.x);
        }

        if (ball.Position.y - ball.Radius < bounds.yMin)
        {
            ball.Position.y = bounds.yMin + ball.Radius;
            ball.Velocity.y = Mathf.Abs(ball.Velocity.y);
        }
        else if (ball.Position.y + ball.Radius > bounds.yMax)
        {
            ball.Position.y = bounds.yMax - ball.Radius;
            ball.Velocity.y = -Mathf.Abs(ball.Velocity.y);
        }
    }

    private void RenderPipelineManager_endCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        // GL에는 셰이더가 없으므로, 유니티 내장 셰이더를 사용해서 라인을 그림
        lineMaterial.SetPass(0);

        // 테두리 그리기
        GL.PushMatrix();
        GL.Begin(GL.LINES);

        GL.Color(lineColor);
        DrawRect(bounds);

        if(showTree && tree != null)
        {
            nodeBounds.Clear();
            tree.CollectBounds(nodeBounds);

            GL.Color(nodeColor);
            foreach (Rect r in nodeBounds)
            {
                DrawRect(r);
            }
        }

        GL.End();

        // 공 그리기
        GL.Begin(GL.TRIANGLES);
        GL.Color(ballColor);
        foreach(Ball ball in balls)
        {
            GL.Color(ball.IsColliding ? collidingColor : ballColor);
            DrawCircle(ball.Position, ball.Radius);
        }
        GL.End();

        GL.PopMatrix();
    }

    private void DrawRect(Rect r)
    {
        Vector3 bottomLeft = new Vector3(r.xMin, r.yMin);
        Vector3 bottomRight = new Vector3(r.xMax, r.yMin);
        Vector3 topRight = new Vector3(r.xMax, r.yMax);
        Vector3 topLeft = new Vector3(r.xMin, r.yMax);  

        GL.Vertex(bottomLeft); GL.Vertex(bottomRight);
        GL.Vertex(bottomRight); GL.Vertex(topRight);
        GL.Vertex(topRight); GL.Vertex(topLeft);
        GL.Vertex(topLeft); GL.Vertex(bottomLeft);
    }

    private void DrawCircle(Vector2 center, float radius)
    {
        float angleStep = 2f * Mathf.PI / CircleSegments;

        for(int i = 0; i < CircleSegments; i++)
        {
            float angle1 = i * angleStep;
            float angle2 = (i + 1) * angleStep;

            GL.Vertex(center);
            GL.Vertex(center + new Vector2(Mathf.Cos(angle1), Mathf.Sin(angle1)) * radius);
            GL.Vertex(center + new Vector2(Mathf.Cos(angle2), Mathf.Sin(angle2)) * radius);
        }
    }

    private void OnGUI()
    {
        if(labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 22 };
            labelStyle.normal.textColor = Color.white;
        }

        float fps = smoothdDeltaTime > 0f ? 1f / smoothdDeltaTime : 0f;

        string text =
            $"Mode : {mode}\n" +
            $"Balls : {ballCount}\n" +
            $"FPS : {fps:F0}\n" +
            $"Collision : {collisionMs:F2} ms\n" +
            $"Checks : {checkCount:N0}\n\n" +
            (isBenchmarking ? "Benchmarking..." : "[Space] Mode\n[T] Tree\n[Up/Down] Balls\n[R] Respawn\n[B] Benchmark");

        GUI.Label(new Rect(10, 10, 900, 300), text, labelStyle);
    }
}
