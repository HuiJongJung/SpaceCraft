using System.Collections.Generic;
using UnityEngine;

public static class PlacementCalculator 
{
    /// 가구의 sizeCentimeters와 cellSize, rotation에 따라
    /// "가로 몇 셀 x 세로(깊이) 몇 셀을 차지하는지" 계산.
    /// - sizeCentimeters.x = width (가로, X)
    /// - sizeCentimeters.z = depth (세로, Z)
    /// - rotation 0/180:  width=X, depth=Z
    /// - rotation 90/270: width=Z, depth=X (회전으로 뒤바뀜)
    public static Vector2Int ComputeFootprintCells(Vector3 sizeCentimeters, float cellSizeMeter, int rotationDeg)
    {
        const float CM_TO_M = 0.01f;

        float widthCm = sizeCentimeters.x;
        float depthCm = sizeCentimeters.z;

        int normalizedRot = Mathf.Abs(rotationDeg) % 360;
        if (normalizedRot == 90 || normalizedRot == 270)
        {
            // 90/270일 때 폭/깊이 스왑
            float tmp = widthCm;
            widthCm = depthCm;
            depthCm = tmp;
        }

        float widthM = widthCm * CM_TO_M;
        float depthM = depthCm * CM_TO_M;

        if (cellSizeMeter <= 0f) cellSizeMeter = 0.1f; // fallback

        int wCells = Mathf.CeilToInt(widthM / cellSizeMeter);
        int dCells = Mathf.CeilToInt(depthM / cellSizeMeter);

        if (wCells < 1) wCells = 1;
        if (dCells < 1) dCells = 1;

        return new Vector2Int(wCells, dCells);
    }

    /// originCell(좌측-하단)과 footprint 셀 크기(sizeInCells)를 기준으로
    /// 가구의 "중심 셀" 을 계산
    public static Vector2Int ComputePivotCell(Vector2Int originCell, Vector2Int sizeInCells)
    {
        int offsetX = (sizeInCells.x - 1) / 2;
        int offsetZ = (sizeInCells.y - 1) / 2;

        Vector2Int pivot = new Vector2Int(
            originCell.x + offsetX,
            originCell.y + offsetZ
        );

        return pivot;
    }

    // 중심점(Pivot)과 크기를 알 때, 왼쪽-아래(Origin) 좌표를 역계산하는 함수
    public static Vector2Int ComputeOriginFromPivot(Vector2Int pivot, Vector2Int size)
    {
        int offsetX = (size.x - 1) / 2;
        int offsetZ = (size.y - 1) / 2;

        return new Vector2Int(pivot.x - offsetX, pivot.y - offsetZ);
    }

    // 회전된 상태에서의 상하좌우 여유 공간(Cell 단위)을 계산
    // 반환 순서: (Bottom, Top, Left, Right)
    public static (int bottom, int top, int left, int right) GetRotatedClearanceInCells(FurnitureItemData item, float cellSize, int rot)
    {
        // 1. cm -> cell 변환 (100cm = 1m)
        // 예: 60cm / 10cm(0.1m) = 6칸
        int cFront = Mathf.CeilToInt(item.clearance.front * 0.01f / cellSize);
        int cBack = Mathf.CeilToInt(item.clearance.back * 0.01f / cellSize);
        int cLeft = Mathf.CeilToInt(item.clearance.left * 0.01f / cellSize);
        int cRight = Mathf.CeilToInt(item.clearance.right * 0.01f / cellSize);

        // 2. 회전 변환 (Grid 기준: Bottom(-Z), Top(+Z), Left(-X), Right(+X))
        int normRot = (rot % 360 + 360) % 360;

        // 0도: Front=Top(+Z), Back=Bottom(-Z), Left=Left(-X), Right=Right(+X)
        if (normRot == 0) return (cBack, cFront, cLeft, cRight);

        // 90도: Front=Right, Back=Left, Left=Top, Right=Bottom
        if (normRot == 90) return (cRight, cLeft, cBack, cFront);

        // 180도: Front=Bottom, Back=Top, Left=Right, Right=Left
        if (normRot == 180) return (cFront, cBack, cRight, cLeft);

        // 270도: Front=Left, Back=Right, Left=Bottom, Right=Top
        if (normRot == 270) return (cLeft, cRight, cFront, cBack);

        return (0, 0, 0, 0);
    }

    /// rotationDeg(0/90/180/270)과 가구 로컬 방향("front/back/left/right")을 받아서
    /// 그리드 기준 방향("top/bottom/left/right") 문자열로 변환.
    public static string GetGridSideByRotation(int rotationDeg, string side)
    {
        // 회전 후 가구의 forward / right 벡터 계산
        Quaternion q = Quaternion.Euler(0f, rotationDeg, 0f);
        Vector3 fwd = q * Vector3.forward; // 가구의 '앞'
        Vector3 right = q * Vector3.right;   // 가구의 '오른쪽'

        // side에 따라 어떤 방향 벡터를 쓸지 결정
        Vector3 dir;
        switch (side)
        {
            case "front":
                dir = fwd;
                break;
            case "back":
                dir = -fwd;
                break;
            case "left":
                dir = -right;
                break;
            case "right":
                dir = right;
                break;
            default:
                return "";
        }

        string closest = ClosestDir(dir);
        Debug.Log($"[GetGridSideByRotation] rotationDeg={rotationDeg}, side={side}, dir={dir}, closestGridSide={closest}");

        return ClosestDir(dir);
    }

    private static string ClosestDir(Vector3 dir)
    {
        Vector3 d = new Vector3(dir.x, 0f, dir.z).normalized;
        if (d.sqrMagnitude < 1e-6f)
            return "";

        float dotF = Vector3.Dot(d, Vector3.forward); // +Z
        float dotB = Vector3.Dot(d, Vector3.back);    // -Z
        float dotL = Vector3.Dot(d, Vector3.left);    // -X
        float dotR = Vector3.Dot(d, Vector3.right);   // +X

        float maxDot = dotF;
        string best = "top"; // +Z

        if (dotB > maxDot) { maxDot = dotB; best = "bottom"; }
        if (dotL > maxDot) { maxDot = dotL; best = "left"; }
        if (dotR > maxDot) { maxDot = dotR; best = "right"; }

        return best;
    }

    public static bool IsWallPlacementRequired(FurnitureItemData item)
    {
        // 4방향 중 하나라도 true면 벽 배치 가구
        return item.wallDir.back || item.wallDir.front || item.wallDir.left || item.wallDir.right;
    }

    public static void ShuffleArray<T>(T[] array)
    {
        for (int i = 0; i < array.Length; i++)
        {
            int rnd = Random.Range(0, array.Length);
            T temp = array[rnd];
            array[rnd] = array[i];
            array[i] = temp;
        }
    }

    public static void ShuffleList<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int rnd = Random.Range(0, list.Count);
            T temp = list[rnd];
            list[rnd] = list[i];
            list[i] = temp;
        }
    }

    public static int GetRequiredWallCount(FurnitureItemData item)
    {
        int count = 0;
        if (item.wallDir.back) count++;
        if (item.wallDir.front) count++;
        if (item.wallDir.left) count++;
        if (item.wallDir.right) count++;
        return count;
    }
}
