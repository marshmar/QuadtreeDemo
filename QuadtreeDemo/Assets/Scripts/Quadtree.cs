using UnityEngine;
using System.Collections.Generic;

public class Quadtree
{
    private readonly Rect bounds;
    private readonly int capacity;
    private readonly int maxDepth;
    private readonly int depth;

    private readonly List<Ball> balls = new List<Ball>();
    private Quadtree[] children;

    bool IsLeaf => children == null;

    public Quadtree(Rect bounds, int capacity, int maxDepth, int depth = 0)
    {
        this.bounds = bounds;
        this.capacity = capacity;
        this.maxDepth = maxDepth;
        this.depth = depth;
    }

    public void Insert(Ball ball)
    {
        // 이미 분할된 노드라면 알맞은 자식에게 넘김
        if (!IsLeaf)
        {
            GetChild(ball.Position).Insert(ball);
            return;
        }

        balls.Add(ball);

        // 용량을 넘었고 아직 더 깊이 내려갈 수 있다면 분할
        if(balls.Count > capacity && depth < maxDepth)
        {
            Subdivide();
        }
    }

    private void Subdivide()
    {
        float x = bounds.xMin;
        float y = bounds.yMin;
        float halfW = bounds.width * 0.5f;
        float halfH = bounds.height * 0.5f;

        children = new Quadtree[4];
        children[0] = new Quadtree(new Rect(x, y, halfW, halfH), capacity, maxDepth, depth + 1); // 왼쪽 아래
        children[1] = new Quadtree(new Rect(x + halfW, y, halfW, halfH), capacity, maxDepth, depth + 1); // 오른쪽 아래
        children[2] = new Quadtree(new Rect(x, y + halfH, halfW, halfH), capacity, maxDepth, depth + 1); // 왼쪽 위
        children[3] = new Quadtree(new Rect(x + halfW, y + halfH, halfW, halfH), capacity, maxDepth, depth + 1); // 오른쪽 위

        // 가지고 있던 공을 자식들에게 재분배
        foreach(Ball ball in balls)
        {
            GetChild(ball.Position).Insert(ball);
        }
        balls.Clear();  
    }

    Quadtree GetChild(Vector2 point)
    {
        Vector2 center = bounds.center;
        bool isRight = point.x >= center.x;
        bool isTop = point.y >= center.y;

        int index = (isRight ? 1 : 0) + (isTop ? 2 : 0);
        return children[index];
    }

    // 디버그 그리기용: 모든 노드의 영역을 모음
    public void CollectBounds(List<Rect> rects)
    {
        rects.Add(bounds);
        if (!IsLeaf)
        {
            foreach (var child in children)
            {
                child.CollectBounds(rects);
            }
        }
    }

    // range와 겹치는 공을 result에 추가
    public void Query(Rect range, List<Ball> result)
    {
        // 이 노드 영역이 범위와 전혀 안 겹치면 종료
        if (!bounds.Overlaps(range))
        {
            return; 
        }

        // 리프면 내 공 중 범위 안에 있는 것만 담음
        if(IsLeaf)
        {
            foreach (Ball ball in balls)
            {
                if(range.Contains(ball.Position))
                {
                    result.Add(ball);
                }
            }
            return;
        }

        // 분할된 노드면 자식들에게 물어봄
        foreach (Quadtree child in children)
        {
            child.Query(range, result);
        }
    }
}
