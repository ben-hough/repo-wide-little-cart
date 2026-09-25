using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;

namespace MrGlim.WideLittleCart
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency("com.github.darmuh.PocketCartPlus", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "MrGlim.WideLittleCart";
        public const string Name = "WideLittleCart";
        public const string Version = "1.0.0";

        internal const string RoomKey = "MrGlim.WLC.scale";
        internal const string SmallItemName = "Item Cart Small";   // shop name "POCKET C.A.R.T."
        internal const string NormalItemName = "Item Cart Medium"; // shop name "C.A.R.T."

        // Measured from the REPO prefabs (floor collider "Inside" of each cart) - used only if runtime measuring fails.
        internal const float FallbackNormalWidth = 1.232f;
        internal const float FallbackSmallWidth = 0.774f;
        internal const float FallbackSmallDepth = 0.786f;

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> WidthMatchesNormalCart;
        internal static ConfigEntry<float> DepthMultiplier;
        internal static ConfigEntry<float> ManualWidthMultiplier;
        internal static ConfigEntry<float> HeightMultiplier;
        internal static ConfigEntry<bool> ScaleHandleWidth;
        internal static ConfigEntry<bool> ApplyToModdedSmallCarts;
        internal static ConfigEntry<bool> DebugLogging;

        private static float _normalWidth = -1f;
        internal static float MeasuredSmallWidth = FallbackSmallWidth;
        private float _publishTimer;
        private Vector3 _published = Vector3.zero;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            Enabled = Config.Bind("General", "Enabled", true, "Resize the vanilla POCKET C.A.R.T. (the little cart, 'Item Cart Small').");
            WidthMatchesNormalCart = Config.Bind("Size", "WidthMatchesNormalCart", true,
                "Make the little cart exactly as wide as the normal C.A.R.T. (measured at runtime from the game's 'Item Cart Medium' prefab).");
            DepthMultiplier = Config.Bind("Size", "DepthMultiplier", 2.0f,
                new ConfigDescription("Front-to-back length relative to the vanilla little cart (2 = twice as deep).", new AcceptableValueRange<float>(0.5f, 4f)));
            ManualWidthMultiplier = Config.Bind("Size", "ManualWidthMultiplier", 0f,
                new ConfigDescription("If > 0, overrides the width factor (relative to the vanilla little cart) instead of matching the normal cart.", new AcceptableValueRange<float>(0f, 4f)));
            HeightMultiplier = Config.Bind("Size", "HeightMultiplier", 1.0f,
                new ConfigDescription("Height relative to the vanilla little cart (1 = unchanged).", new AcceptableValueRange<float>(0.5f, 3f)));
            ScaleHandleWidth = Config.Bind("Visuals", "ScaleHandleWidth", true,
                "Stretch the handle (and its grab area) to the new width. The handle is moved to the new back edge either way; its depth/thickness is never stretched.");
            ApplyToModdedSmallCarts = Config.Bind("General", "ApplyToModdedSmallCarts", false,
                "Also resize other small carts (e.g. PocketCartPlus's POCKET C.A.R.T. PLUS, which does its own scaling). Default off: only the vanilla little cart.");
            DebugLogging = Config.Bind("Debug", "DebugLogging", false, "Log measurements and every resized cart.");

            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(CartPatches));
            Log.LogInfo($"{Name} {Version} loaded.");
        }

        internal static void Debug(string m) { if (DebugLogging != null && DebugLogging.Value) Log.LogInfo(m); }

        private void Update()
        {
            // Host publishes its scale so guests use identical numbers (physics is host-authoritative).
            _publishTimer -= Time.unscaledDeltaTime;
            if (_publishTimer > 0f) return;
            _publishTimer = 2f;
            try
            {
                if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) { _published = Vector3.zero; return; }
                Vector3 s = Enabled.Value ? LocalScaleFactors(MeasuredSmallWidth) : Vector3.one;
                var room = PhotonNetwork.CurrentRoom;
                bool present = room.CustomProperties != null && room.CustomProperties.ContainsKey(RoomKey);
                if (present && (s - _published).sqrMagnitude < 1e-8f) return;
                room.SetCustomProperties(new PhotonHashtable { { RoomKey, new float[] { s.x, s.y, s.z } } });
                _published = s;
                Debug($"Published host scale {s:F4}");
            }
            catch (Exception e) { Debug("Publish failed: " + e.Message); }
        }

        // ------------------------------------------------------------------ measuring

        /// <summary>X (width) / Z (depth) extents of a cart's floor collider ("Inside/Semi Box Collider"), in the cart's own local space.</summary>
        internal static bool MeasureFloor(Transform cartRoot, out float width, out float depth)
        {
            width = depth = 0f;
            if (cartRoot == null) return false;
            Transform inside = null;
            foreach (Transform c in cartRoot) if (c.name == "Inside") { inside = c; break; }
            if (inside == null) return false;
            var box = inside.GetComponentInChildren<BoxCollider>(true);
            if (box == null) return false;
            Matrix4x4 toRoot = cartRoot.worldToLocalMatrix * box.transform.localToWorldMatrix;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                var corner = box.center + Vector3.Scale(box.size * 0.5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = toRoot.MultiplyPoint3x4(corner);
                min = Vector3.Min(min, p); max = Vector3.Max(max, p);
            }
            width = max.x - min.x;
            depth = max.z - min.z;
            return width > 0.01f && depth > 0.01f;
        }

        internal static float NormalCartWidth()
        {
            if (_normalWidth > 0f) return _normalWidth;
            try
            {
                var sm = StatsManager.instance;
                if (sm != null && sm.itemDictionary != null)
                {
                    foreach (var kv in sm.itemDictionary)
                    {
                        var item = kv.Value;
                        if (item == null || (kv.Key != NormalItemName && item.name != NormalItemName)) continue;
                        var prefab = item.prefab != null ? item.prefab.Prefab : null;
                        var cart = prefab != null ? prefab.GetComponentInChildren<PhysGrabCart>(true) : null;
                        if (cart != null && !cart.isSmallCart && MeasureFloor(cart.transform, out float w, out float d))
                        {
                            _normalWidth = w;
                            Log.LogInfo($"Measured normal C.A.R.T. floor from prefab: width {w:F3} m, depth {d:F3} m");
                            return _normalWidth;
                        }
                    }
                }
            }
            catch (Exception e) { Debug("Prefab measure failed: " + e.Message); }
            return FallbackNormalWidth; // not cached, retry later
        }

        /// <summary>Scale factors (x = width, y = height, z = depth) relative to the vanilla little cart, from this client's config.</summary>
        internal static Vector3 LocalScaleFactors(float smallWidth = FallbackSmallWidth)
        {
            float sx = 1f;
            if (ManualWidthMultiplier.Value > 0f) sx = ManualWidthMultiplier.Value;
            else if (WidthMatchesNormalCart.Value && smallWidth > 0.01f) sx = NormalCartWidth() / smallWidth;
            return new Vector3(Mathf.Clamp(sx, 0.25f, 4f), Mathf.Clamp(HeightMultiplier.Value, 0.25f, 4f), Mathf.Clamp(DepthMultiplier.Value, 0.25f, 4f));
        }

        /// <summary>Returns false if this client should not resize (disabled, or guest whose host doesn't run the mod).</summary>
        internal static bool ResolveScale(float smallWidth, out Vector3 s)
        {
            s = Vector3.one;
            if (!Enabled.Value) return false;
            if (SemiFunc.IsMultiplayer() && PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
            {
                // Host-gated: guests never pick their own size. They use exactly what the host publishes,
                // and stay vanilla when the host doesn't run the mod (no client-only advantage, no desync).
                var props = PhotonNetwork.CurrentRoom.CustomProperties;
                if (props != null && props.TryGetValue(RoomKey, out object o) && o is float[] f && f.Length == 3)
                {
                    s = new Vector3(f[0], f[1], f[2]);
                    return (s - Vector3.one).sqrMagnitude > 1e-6f;
                }
                Log.LogWarning("Host is not running WideLittleCart (no size published) - leaving the little cart vanilla (the host decides the size).");
                return false;
            }
            s = LocalScaleFactors(smallWidth);
            return (s - Vector3.one).sqrMagnitude > 1e-6f;
        }
    }

    /// <summary>Marker + per-cart data.</summary>
    public class WideLittleCartMarker : MonoBehaviour
    {
        public Vector3 scale = Vector3.one;
        public float extraPushDistance;
    }

    [HarmonyPatch]
    internal static class CartPatches
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(PhysGrabCart), "Start")]
        private static void StartPostfix(PhysGrabCart __instance)
        {
            try { Apply(__instance); }
            catch (Exception e) { Plugin.Log.LogError("Failed to resize little cart: " + e); }
        }

        private static bool IsVanillaLittleCart(PhysGrabCart cart)
        {
            if (!cart.isSmallCart) return false;
            if (Plugin.ApplyToModdedSmallCarts.Value) return true;
            foreach (var mb in cart.GetComponents<MonoBehaviour>())
                if (mb != null && mb.GetType().Name == "PocketCartUpgradeSize") return false; // PocketCartPlus PLUS variant
            var attrs = cart.GetComponent<ItemAttributes>();
            if (attrs != null && attrs.item != null) return attrs.item.name == Plugin.SmallItemName;
            return cart.gameObject.name.StartsWith(Plugin.SmallItemName);
        }

        private static void Apply(PhysGrabCart cart)
        {
            if (cart == null || cart.GetComponent<WideLittleCartMarker>() != null) return;
            if (!IsVanillaLittleCart(cart)) return;

            Transform root = cart.transform;
            if (!Plugin.MeasureFloor(root, out float smallW, out float smallD)) { smallW = Plugin.FallbackSmallWidth; smallD = Plugin.FallbackSmallDepth; }
            else Plugin.MeasuredSmallWidth = smallW;
            if (!Plugin.ResolveScale(smallW, out Vector3 s)) return;

            var marker = cart.gameObject.AddComponent<WideLittleCartMarker>();
            marker.scale = s;

            Transform screen = cart.valueScreen != null ? cart.valueScreen.transform : null;
            Vector3 handleScale = Plugin.ScaleHandleWidth.Value ? new Vector3(s.x, 1f, 1f) : Vector3.one;
            float grabPointZ = cart.cartGrabPoint != null ? cart.cartGrabPoint.localPosition.z : -0.423f;

            // Only direct children are touched: position scaled in cart space, local scale multiplied by the
            // cart-space factor along each of the child's own axes. The cart root stays at scale 1, so the game's
            // (and PocketCartPlus's) equip/unequip scale animations and "localScale == Vector3.one" checks are unaffected.
            var children = new List<Transform>();
            foreach (Transform c in root) children.Add(c);
            foreach (var child in children)
            {
                string n = child.name;
                if (n.StartsWith("Item Volume") || n.StartsWith("SemiIcon")) continue;          // shop-slot volume / icon camera
                if (child == screen || n == "Screen") { child.localPosition = Vector3.Scale(child.localPosition, s); continue; } // keep screen undistorted
                if (n == "Cart Handle" || n == "Grab Area") { ScaleChild(child, s, handleScale); continue; }
                if (n == "Cart Grab Point" || child == cart.cartGrabPoint || child == cart.handlePoint)
                { child.localPosition = Vector3.Scale(child.localPosition, s); continue; }
                ScaleChild(child, s, s);
            }

            // PhysGrabCart re-applies the thrust effect's scale every frame from a cached value.
            if (cart.thrustEffect != null) cart.thrustEffectOriginalScale = cart.thrustEffect.localScale;

            marker.extraPushDistance = Mathf.Abs(grabPointZ) * (s.z - 1f);

            Physics.SyncTransforms();
            var rb = cart.GetComponent<Rigidbody>();
            if (rb != null) { rb.ResetCenterOfMass(); rb.ResetInertiaTensor(); }
            RefreshPhysGrabObjectBounds(cart.GetComponent<PhysGrabObject>());

            Plugin.MeasureFloor(root, out float newW, out float newD);
            Plugin.Log.LogInfo($"Resized {cart.gameObject.name}: factors width x{s.x:F3}, height x{s.y:F3}, depth x{s.z:F3} | floor {smallW:F3}x{smallD:F3} m -> {newW:F3}x{newD:F3} m | push distance +{marker.extraPushDistance:F3} m");
        }

        /// <summary>Scale a direct child: position by posScale, local scale by the per-own-axis factors of shapeScale; keep capsule/sphere radii from growing.</summary>
        private static void ScaleChild(Transform child, Vector3 posScale, Vector3 shapeScale)
        {
            var caps = child.GetComponentsInChildren<CapsuleCollider>(true);
            var spheres = child.GetComponentsInChildren<SphereCollider>(true);
            var capBefore = caps.Select(c => CapsuleRadialScale(c)).ToArray();
            var sphBefore = spheres.Select(c => MaxAbs(c.transform.lossyScale)).ToArray();

            child.localPosition = Vector3.Scale(child.localPosition, posScale);
            Quaternion q = child.localRotation;
            Vector3 f = new Vector3(
                Vector3.Scale(shapeScale, q * Vector3.right).magnitude,
                Vector3.Scale(shapeScale, q * Vector3.up).magnitude,
                Vector3.Scale(shapeScale, q * Vector3.forward).magnitude);
            child.localScale = Vector3.Scale(child.localScale, f);

            // Non-uniform scale makes Unity inflate capsule/sphere radii (it uses the largest axis), which would
            // lift the cart off its wheels. Keep the original world radius; lengths still stretch.
            for (int i = 0; i < caps.Length; i++)
            {
                float after = CapsuleRadialScale(caps[i]);
                if (after > 1e-5f) caps[i].radius *= capBefore[i] / after;
            }
            for (int i = 0; i < spheres.Length; i++)
            {
                float after = MaxAbs(spheres[i].transform.lossyScale);
                if (after > 1e-5f) spheres[i].radius *= sphBefore[i] / after;
            }
        }

        private static float MaxAbs(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));

        private static float CapsuleRadialScale(CapsuleCollider c)
        {
            Vector3 l = c.transform.lossyScale;
            switch (c.direction)
            {
                case 0: return Mathf.Max(Mathf.Abs(l.y), Mathf.Abs(l.z));
                case 1: return Mathf.Max(Mathf.Abs(l.x), Mathf.Abs(l.z));
                default: return Mathf.Max(Mathf.Abs(l.x), Mathf.Abs(l.y));
            }
        }

        /// <summary>Recompute the size data PhysGrabObject caches from its colliders at Start (same math as the game).</summary>
        private static void RefreshPhysGrabObjectBounds(PhysGrabObject pgo)
        {
            if (pgo == null) return;
            try
            {
                Transform t = pgo.transform;
                Quaternion rot = t.rotation;
                t.rotation = Quaternion.identity;
                Physics.SyncTransforms();
                Bounds b = default;
                bool any = false;
                foreach (var col in pgo.GetComponentsInChildren<Collider>())
                {
                    if (col.isTrigger) continue;
                    if (any) b.Encapsulate(col.bounds); else { b = col.bounds; any = true; }
                }
                if (any)
                {
                    pgo.itemHeightY = b.size.y;
                    pgo.itemWidthX = b.size.x;
                    pgo.itemLengthZ = b.size.z;
                    pgo.boundingBox = b.size;
                    pgo.midPointOffset = t.InverseTransformPoint(b.center);
                }
                t.rotation = rot;
                Physics.SyncTransforms();
            }
            catch (Exception e) { Plugin.Debug("Bounds refresh failed: " + e.Message); }
        }

        // ---------------------------------------------------------------- pushing distance (host runs CartSteer)

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(PhysGrabCart), "CartSteer")]
        private static IEnumerable<CodeInstruction> CartSteerTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var lerp = AccessTools.Method(typeof(Mathf), nameof(Mathf.Lerp), new[] { typeof(float), typeof(float), typeof(float) });
            var extra = AccessTools.Method(typeof(CartPatches), nameof(ExtraPush));
            bool done = false;
            foreach (var ins in instructions)
            {
                yield return ins;
                if (!done && ins.Calls(lerp))
                {
                    // num4 = Mathf.Lerp(num, num2, t) + ExtraPush(this)
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, extra);
                    yield return new CodeInstruction(OpCodes.Add);
                    done = true;
                }
            }
            if (!done) Plugin.Log.LogWarning("CartSteer: Mathf.Lerp not found - hold distance not adjusted for the deeper cart.");
        }

        private static float ExtraPush(PhysGrabCart cart)
        {
            var m = cart != null ? cart.GetComponent<WideLittleCartMarker>() : null;
            return m != null ? m.extraPushDistance : 0f;
        }
    }
}
