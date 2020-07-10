#if UNITY_EDITOR
﻿
using System;
 using System.Runtime.InteropServices;
 using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.Networking.PlayerConnection;
using UnityEngine;
using UnityEngine.Networking.PlayerConnection;

namespace Unity.Tiny.Rendering
{

    /**
    * Receives data from the PlayerConnection to make changes to the scene view.
    */
    [InitializeOnLoad]
    static class SceneViewConnectionService
    {
        static bool s_IsSyncCameraListenerRegistered;

        static SceneViewConnectionService()
        {
            RegisterSyncCameraCallbackListener();
        }

        static void AlignViewToObject(Vector3 cameraLoc, Quaternion cameraRot, float fov)
        {
            SceneView.lastActiveSceneView.LookAt(
                cameraLoc + (cameraRot * Vector3.forward) * GetPerspectiveCameraDistance(16, fov),
                cameraRot);
            SceneView.lastActiveSceneView.Repaint();
        }

        /**
         * Based on UnityEditor.SceneView.cs
         * 0bf6c820627311c279ac50e1ac3f7bfe7f33b299
         */
        static float GetPerspectiveCameraDistance(float objectSize, float fov)
        {
            return objectSize / Mathf.Sin(fov * 0.5f * Mathf.Deg2Rad);
        }

        /**
         * Callback function that reads the camera data provided by the player connection
         * and moves the scene camera to its position.
         */
        static unsafe void SyncSceneCameraToGameView(MessageEventArgs args)
        {

            CameraSynchronizationMessage camInfo;

            // read float3 location, quaternion rotation, and float fov from struct
            fixed (byte* pOut = args.data)
            {
                UnsafeUtility.CopyPtrToStructure(pOut, out camInfo);
            }

            AlignViewToObject(camInfo.position, camInfo.rotation, camInfo.fovDegrees);
        }

        static void RemoveCallBackOnQuit()
        {
            EditorConnection.instance.Unregister(SharedCameraSyncInfo.syncCameraGuid, SyncSceneCameraToGameView);
        }

        static void RegisterSyncCameraCallbackListener()
        {
            if (!s_IsSyncCameraListenerRegistered)
            {
                s_IsSyncCameraListenerRegistered = true;
                EditorConnection.instance.Register(SharedCameraSyncInfo.syncCameraGuid, SyncSceneCameraToGameView);
                EditorApplication.quitting += RemoveCallBackOnQuit;
            }
        }
    }
}
#endif
