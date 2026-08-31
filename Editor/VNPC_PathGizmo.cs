#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VNPC;

public static class VNPC_PathGizmo
{
    [DrawGizmo(GizmoType.Selected)]
    private static void DrawManagerPaths(VNPC_Manager manager, GizmoType gizmoType)
    {
        if (manager == null || manager.paths == null) return;

        Color previousColor = Handles.color;
        for (int pathIndex = 0; pathIndex < manager.paths.Length; pathIndex++)
        {
            Transform path = manager.paths[pathIndex];
            if (path == null) continue;

            int pointCount = path.childCount;
            Color pathColor = Color.HSVToRGB((pathIndex * 0.173f) % 1f, 0.75f, 1f);
            Handles.color = pathColor;

            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                Transform point = path.GetChild(pointIndex);
                Handles.Label(point.position, "P" + pointIndex);
            }

            if (pointCount == 2)
            {
                DrawConnection(path.GetChild(0).position, path.GetChild(1).position);
                continue;
            }

            if (pointCount < 3) continue;
            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                Vector3 from = path.GetChild(pointIndex).position;
                Vector3 to = path.GetChild((pointIndex + 1) % pointCount).position;
                DrawConnection(from, to);
            }
        }
        Handles.color = previousColor;
    }

    private static void DrawConnection(Vector3 from, Vector3 to)
    {
        Handles.DrawLine(from, to);
        Vector3 direction = to - from;
        if (direction.sqrMagnitude <= 0.000001f) return;

        Vector3 midpoint = (from + to) * 0.5f;
        float size = HandleUtility.GetHandleSize(midpoint) * 0.12f;
        Handles.ArrowHandleCap(0, midpoint, Quaternion.LookRotation(direction), size, EventType.Repaint);
    }
}
#endif
