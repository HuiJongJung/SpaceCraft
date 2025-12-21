using System.Collections.Generic;
using UnityEngine;

public static class PlacementCalculator 
{
    public static Vector2Int ComputeFootprintCells(Vector3 sizeCentimeters, float cellSizeMeter, int rotationDeg)
    {
        const float CM_TO_M = 0.01f;

        float widthCm = sizeCentimeters.x;
        float depthCm = sizeCentimeters.z;

        int normalizedRot = Mathf.Abs(rotationDeg) % 360;
        if (normalizedRot == 90 || normalizedRot == 270)
        {
            float tmp = widthCm;
            widthCm = depthCm;
            depthCm = tmp;
        }

        float widthM = widthCm * CM_TO_M;
        float depthM = depthCm * CM_TO_M;

        if (cellSizeMeter <= 0f) cellSizeMeter = 0.1f;

        int wCells = Mathf.CeilToInt(widthM / cellSizeMeter);
        int dCells = Mathf.CeilToInt(depthM / cellSizeMeter);

        if (wCells < 1) wCells = 1;
        if (dCells < 1) dCells = 1;

        return new Vector2Int(wCells, dCells);
    }

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

    public static Vector2Int ComputeOriginFromPivot(Vector2Int pivot, Vector2Int size)
    {
        int offsetX = (size.x - 1) / 2;
        int offsetZ = (size.y - 1) / 2;

        return new Vector2Int(pivot.x - offsetX, pivot.y - offsetZ);
    }

    public static (int bottom, int top, int left, int right) GetRotatedClearanceInCells(FurnitureItemData item, float cellSize, int rot)
    {
        int cFront = Mathf.CeilToInt(item.clearance.front * 0.01f / cellSize);
        int cBack = Mathf.CeilToInt(item.clearance.back * 0.01f / cellSize);
        int cLeft = Mathf.CeilToInt(item.clearance.left * 0.01f / cellSize);
        int cRight = Mathf.CeilToInt(item.clearance.right * 0.01f / cellSize);

        int normRot = (rot % 360 + 360) % 360;

        if (normRot == 0) return (cBack, cFront, cLeft, cRight);
        if (normRot == 90) return (cRight, cLeft, cBack, cFront);
        if (normRot == 180) return (cFront, cBack, cRight, cLeft);
        if (normRot == 270) return (cLeft, cRight, cFront, cBack);

        return (0, 0, 0, 0);
    }

    public static string GetGridSideByRotation(int rotationDeg, string side)
    {
        Quaternion q = Quaternion.Euler(0f, rotationDeg, 0f);
        Vector3 fwd = q * Vector3.forward; 
        Vector3 right = q * Vector3.right;   

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
