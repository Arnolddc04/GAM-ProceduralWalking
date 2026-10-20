using System;
using System.Collections.Generic;
using UnityEngine;

namespace GAMStudio.ProceduralWalking
{
    /// <summary>
    /// Finds leg chains in an arbitrary rig hierarchy.
    /// A "hub" bone with two or more leg-like child chains (named like legs, or reaching downwards)
    /// is treated as a leg root; each chain follows the longest path from the hub's child to a leaf.
    /// </summary>
    public static class LegDetector
    {
        public struct LegCandidate
        {
            public Transform Root;
            public Transform Tip;
            public int BoneCount;
            public int Group;
        }

        static readonly string[] s_LegKeywords =
        {
            "leg", "foot", "feet", "toe", "claw", "tarsus", "tibia", "femur", "coxa", "trochanter",
            "metatarsus", "patella", "thigh", "shin", "calf", "knee", "ankle",
        };

        static readonly string[] s_ExcludeKeywords =
        {
            "head", "tail", "spine", "neck", "jaw", "eye", "antenna", "mandible", "abdomen", "wing",
            "fang", "chelicera", "pedipalp", "palp", "hair", "target", "pole", "hint",
        };

        /// <param name="root">The walker body (or model root).</param>
        /// <param name="minBones">Minimum bones (segments) per leg. Chains need minBones + 1 transforms.</param>
        public static List<LegCandidate> Detect(Transform root, int minBones = 2)
        {
            var result = new List<LegCandidate>();
            if (root == null)
                return result;

            var heights = new Dictionary<Transform, int>();
            var claimed = new List<Transform>();
            var queue = new Queue<Transform>();
            var children = new List<Transform>();
            var chains = new List<List<Transform>>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                Transform hub = queue.Dequeue();
                GetBoneChildren(hub, children);

                chains.Clear();
                foreach (Transform child in children)
                {
                    List<Transform> chain = LongestPath(child, heights);
                    if (IsLegLike(root, chain, minBones))
                        chains.Add(chain);
                }

                if (chains.Count >= 2)
                {
                    foreach (List<Transform> chain in chains)
                    {
                        result.Add(new LegCandidate
                        {
                            Root = chain[0],
                            Tip = chain[chain.Count - 1],
                            BoneCount = chain.Count - 1,
                        });
                        claimed.Add(chain[0]);
                    }
                }

                foreach (Transform child in children)
                {
                    if (!claimed.Contains(child))
                        queue.Enqueue(child);
                }
            }

            var rootPositions = new Vector3[result.Count];
            for (int i = 0; i < result.Count; i++)
                rootPositions[i] = result[i].Root.position;
            int[] groups = ComputeAlternatingGroups(root, rootPositions);
            for (int i = 0; i < result.Count; i++)
            {
                LegCandidate candidate = result[i];
                candidate.Group = groups[i];
                result[i] = candidate;
            }

            return result;
        }

        /// <summary>
        /// Two-group alternating gait (tripod for 6 legs, alternating tetrapod for 8): legs are split into
        /// left and right by the body's local X, sorted front to back, and alternate along each side
        /// with the opposite phase on the other side.
        /// </summary>
        public static int[] ComputeAlternatingGroups(Transform body, IReadOnlyList<Vector3> legRootPositions)
        {
            int count = legRootPositions.Count;
            var groups = new int[count];
            var left = new List<int>();
            var right = new List<int>();
            var local = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                local[i] = body.InverseTransformPoint(legRootPositions[i]);
                (local[i].x < 0f ? left : right).Add(i);
            }

            Comparison<int> frontToBack = (a, b) => local[b].z.CompareTo(local[a].z);
            left.Sort(frontToBack);
            right.Sort(frontToBack);

            for (int i = 0; i < left.Count; i++)
                groups[left[i]] = i % 2;
            for (int i = 0; i < right.Count; i++)
                groups[right[i]] = (i + 1) % 2;
            return groups;
        }

        static void GetBoneChildren(Transform parent, List<Transform> result)
        {
            result.Clear();
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (IsBone(child))
                    result.Add(child);
            }
        }

        /// <summary>Anything that is not a mesh, collider, camera or light counts as a bone.</summary>
        static bool IsBone(Transform transform)
        {
            return !transform.TryGetComponent(out Renderer _)
                && !transform.TryGetComponent(out Collider _)
                && !transform.TryGetComponent(out Camera _)
                && !transform.TryGetComponent(out Light _);
        }

        static int Height(Transform transform, Dictionary<Transform, int> cache)
        {
            if (cache.TryGetValue(transform, out int cached))
                return cached;
            int height = 0;
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (IsBone(child))
                    height = Mathf.Max(height, Height(child, cache) + 1);
            }
            cache[transform] = height;
            return height;
        }

        static List<Transform> LongestPath(Transform start, Dictionary<Transform, int> heights)
        {
            var path = new List<Transform> { start };
            Transform current = start;
            while (true)
            {
                Transform deepest = null;
                int deepestHeight = -1;
                for (int i = 0; i < current.childCount; i++)
                {
                    Transform child = current.GetChild(i);
                    if (!IsBone(child))
                        continue;
                    int height = Height(child, heights);
                    if (height > deepestHeight)
                    {
                        deepest = child;
                        deepestHeight = height;
                    }
                }

                if (deepest == null)
                    return path;
                path.Add(deepest);
                current = deepest;
            }
        }

        static bool IsLegLike(Transform root, List<Transform> chain, int minBones)
        {
            if (chain.Count < minBones + 1)
                return false;

            bool legName = false;
            bool excludedName = false;
            foreach (Transform bone in chain)
            {
                string name = bone.name.ToLowerInvariant();
                legName |= ContainsAny(name, s_LegKeywords);
                excludedName |= ContainsAny(name, s_ExcludeKeywords);
            }

            if (legName)
                return true;
            if (excludedName)
                return false;

            float length = 0f;
            for (int i = 0; i < chain.Count - 1; i++)
                length += Vector3.Distance(chain[i].position, chain[i + 1].position);
            float drop = Vector3.Dot(chain[0].position - chain[chain.Count - 1].position, root.up);
            return length > 0f && drop > length * 0.1f;
        }

        static bool ContainsAny(string value, string[] keywords)
        {
            foreach (string keyword in keywords)
            {
                if (value.Contains(keyword))
                    return true;
            }
            return false;
        }
    }
}
