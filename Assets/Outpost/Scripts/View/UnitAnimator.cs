using System.Collections.Generic;
using UnityEngine;
using Outpost.Content;

namespace Outpost.View
{
    /// <summary>
    /// One model on screen that can move: it swaps between its exported poses (stand, step, attack),
    /// turns its turret parts toward a target and spins its rotors. Used for every squad member,
    /// vehicle, aircraft and base turret. A model with no poses or parts simply stands still.
    /// </summary>
    public class ModelRig
    {
        public const int Stand = 0, Step = 1, Attack = 2;
        static readonly string[] PoseNames = { null, "walk1", "attack" };

        class Piece
        {
            public Transform t;
            public MeshFilter filter;
            public MeshRenderer renderer;
            public string kind;                       // null (body), "turret" or "spin"
            public readonly ModelMesh[] poses = new ModelMesh[3];
            public int shown;
        }

        public readonly Transform root;
        readonly Piece body;
        readonly List<Piece> parts = new List<Piece>();
        float turretYaw;

        public bool HasTurret { get; private set; }

        ModelRig(Transform root, string model)
        {
            this.root = root;
            body = Make(root, model, null);
            foreach (var p in ModelLibrary.Parts(model))
            {
                var child = root.Find(p.name);
                if (child == null) continue;
                parts.Add(Make(child, p.model, p.kind));
                if (p.kind == "turret") HasTurret = true;
            }
        }

        static Piece Make(Transform t, string model, string kind)
        {
            var piece = new Piece { t = t, kind = kind, filter = t.GetComponent<MeshFilter>(), renderer = t.GetComponent<MeshRenderer>() };
            piece.poses[Stand] = ModelLibrary.Get(model);
            for (int i = 1; i < PoseNames.Length; i++) piece.poses[i] = ModelLibrary.Get(ModelLibrary.PoseName(model, PoseNames[i]));
            return piece;
        }

        /// <summary>Spawns the model under parent and wraps it; null if the model wasn't exported.</summary>
        public static ModelRig Create(string model, Transform parent)
        {
            var go = ModelLibrary.Spawn(model, parent);
            return go == null ? null : new ModelRig(go.transform, model);
        }

        /// <summary>Shows a pose on every piece that has it; pieces without it stay in their standing pose.</summary>
        public void SetPose(int pose)
        {
            Show(body, pose);
            foreach (var p in parts) Show(p, pose);
        }

        static void Show(Piece p, int pose)
        {
            if (p.poses[pose] == null) pose = Stand;
            if (p.shown == pose || p.poses[pose] == null || p.filter == null) return;
            p.shown = pose;
            p.filter.sharedMesh = p.poses[pose].mesh;
            p.renderer.sharedMaterials = p.poses[pose].materials;
        }

        /// <summary>Turns turret parts toward a yaw relative to the body (degrees), easing by k (0..1).</summary>
        public void Aim(float localYaw, float k)
        {
            if (!HasTurret) return;
            turretYaw = Mathf.LerpAngle(turretYaw, localYaw, k);
            var rot = Quaternion.Euler(0f, turretYaw, 0f);
            foreach (var p in parts)
                if (p.kind == "turret") p.t.localRotation = rot;
        }

        public void Spin(float degrees)
        {
            foreach (var p in parts)
                if (p.kind == "spin") p.t.Rotate(0f, degrees, 0f, Space.Self);
        }
    }
}
