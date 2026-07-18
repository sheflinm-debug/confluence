using System.Collections.Generic;
using UnityEngine;

/// Deterministic procedural morphology generator — appearance-generation-spec §3 (Era 1 organism
/// tier). Turns an organism's sparse state (symmetry, motility, appendage class, structural body
/// plan) plus a per-lineage seed into a noise-deformed blob mesh, so different lineages look
/// genuinely distinct and conspecifics resemble one another — instead of every organism being an
/// identical capsule "gumdrop."
///
/// Core principle from the spec: STATE + SEED → GENERATOR → GEOMETRY, where the generator is a pure
/// function (same inputs always produce the same mesh). Non-determinism lives entirely in the seed
/// (per lineage), influence lives in the state. Meshes are cached by signature so the whole
/// population shares a bounded set of meshes rather than allocating one per agent.
public static class MorphologyGenerator
{
    // appearance-generation-spec §2.2 M1: Biradial and ColonialModular added alongside the original
    // three. Biradial is a parametric variant (ctenophore-style dual symmetry plane) handled inside
    // the normal deform loop; ColonialModular is a genuinely different topology (§2.6's "graph/
    // L-system" branch) and short-circuits Build() entirely — see BuildColonial below.
    public enum Symmetry { Radial, Bilateral, Asymmetric, Biradial, ColonialModular }

    // Base unit-icosphere geometry, generated once and shared by every deformation.
    private static Vector3[] _unitVerts;
    private static int[] _baseTris;

    private static readonly Dictionary<int, Mesh> _cache = new Dictionary<int, Mesh>();

    private const float BaseRadius = 0.5f; // matches the old capsule's ~0.5 radius so on-screen size is unchanged

    /// Returns a cached deformed mesh for the given morphology parameters. Safe to call every spawn;
    /// only the first call per unique signature actually builds a mesh. segmentation/integument/
    /// pairCount (appearance-generation-spec §2.2 M2/M9-M10/M5) default to 0 ("axis not yet
    /// populated") so existing call sites keep working unchanged. networkForeshadowBucket (§3.3,
    /// 0-10) is a SEPARATE, gradual signal from the hard ColonialModular symmetry switch — a
    /// Distributed-architecture lineage that hasn't (or will never) flip to ColonialModular still
    /// buds a few network-like nodules as Era 2 progresses, so the eventual Living-Reef/mycelial-
    /// network transition doesn't read as an arbitrary Era 3 snap.
    public static Mesh GetMesh(int lineageSeed, Symmetry symmetry, bool motile, int appendageLevel, int structureType,
        int segmentation = 0, int integument = 0, int pairCount = 0, int networkForeshadowBucket = 0)
    {
        EnsureBase();
        int key = HashKey(lineageSeed, (int)symmetry, motile ? 1 : 0, appendageLevel, structureType, segmentation, integument, pairCount, networkForeshadowBucket);
        if (_cache.TryGetValue(key, out var cached) && cached != null) return cached;
        Mesh m = Build(lineageSeed, symmetry, motile, appendageLevel, structureType, segmentation, integument, pairCount, networkForeshadowBucket);
        _cache[key] = m;
        return m;
    }

    // ── Deformation ────────────────────────────────────────────────────────────

    private static Mesh Build(int lineageSeed, Symmetry sym, bool motile, int appendage, int structure,
        int segmentation = 0, int integument = 0, int pairCount = 0, int networkForeshadowBucket = 0)
    {
        // ColonialModular is a fundamentally different topology (§2.6's graph/L-system branch, not
        // a deformed single blob) — it never runs the per-vertex deform loop below at all.
        if (sym == Symmetry.ColonialModular)
            return BuildColonial(lineageSeed, structure, integument);

        // Deterministic per-lineage RNG: identical seed → identical body, different seed → different.
        var rng = new System.Random(HashKey(lineageSeed, (int)sym, motile ? 1 : 0, appendage, structure, segmentation, integument, pairCount, networkForeshadowBucket));
        float R() => (float)rng.NextDouble();

        // Per-axis base scaling encodes body symmetry.
        Vector3 axisScale;
        switch (sym)
        {
            case Symmetry.Bilateral:
                // Elongated head-to-tail (worm/bilaterian), narrower across.
                axisScale = new Vector3(0.62f + 0.15f * R(), 0.62f + 0.15f * R(), 1.45f + 0.35f * R());
                break;
            case Symmetry.Biradial:
                // Ctenophore-style dual symmetry plane — flatter and wider across the second axis
                // than pure Bilateral, so the two symmetry planes both read visually instead of one
                // dominating (a comb-row-like silhouette rather than a worm-like one).
                axisScale = new Vector3(0.85f + 0.15f * R(), 0.55f + 0.1f * R(), 1.15f + 0.25f * R());
                break;
            case Symmetry.Asymmetric:
                axisScale = new Vector3(0.7f + 0.7f * R(), 0.7f + 0.7f * R(), 0.7f + 0.7f * R());
                break;
            default: // Radial — dome/disc, roughly rotationally symmetric.
                axisScale = new Vector3(1f, 1.05f + 0.35f * R(), 1f);
                break;
        }

        // Structural body plan modulates surface roughness and elongation — appearance-generation-
        // spec §2.7's M3 rig-treatment table. Enum values (AgentController.BodyPlanType):
        // 0=None(hydrostatic) 1=Exoskeleton(exo-chitin) 2=Shell(exo-mineral) 3=Endoskeleton
        // (endo-cartilage) 4=SoftBody 5=EndoMineralized 6=MixedArmor 7=Crystalline.
        float noiseAmp = 0.13f;
        bool patchyArmor = false;
        switch (structure)
        {
            case 1: case 2: noiseAmp = 0.06f; break;          // exo-chitin / exo-mineral — hard-shell plated, smoother
            case 3: axisScale.z *= 1.2f; noiseAmp = 0.11f; break; // endo-cartilage — internal rig, exterior stays soft
            case 4: noiseAmp = 0.20f; break;                  // SoftBody — wobblier
            case 5: axisScale.z *= 1.3f; noiseAmp = 0.09f; break; // endo-mineralized — larger, internal rigidity firms the exterior slightly more than cartilage alone
            case 6: noiseAmp = 0.08f; patchyArmor = true; break;  // mixed-armor — hard plates over a soft/endo base, irregular patch coverage not uniform hardness
            case 7: noiseAmp = 0.02f; break;                  // crystalline — near-zero organic noise; faceted look comes from BuildFaceted below, not this deformation
        }

        float noiseFreq = 1.6f + 2.4f * R();
        Vector3 noiseOffset = new Vector3(R() * 100f, R() * 100f, R() * 100f);
        int ridgeCount = 3 + rng.Next(0, 5);
        float ridgeAmp = sym == Symmetry.Radial ? 0.06f + 0.06f * R() : 0f;

        // appearance-generation-spec §2.2 M2: segmentation reads as ring modulation along the main
        // (z) body axis. AgentController.SegmentationType: 0=Unsegmented 1=Metameric 2=Tagmatized
        // 3=SecondarilySimplified. Metameric = many uniform rings (repeated segments); Tagmatized =
        // few sharp-boundaried fused regions (functional tagmata), not more rings but BIGGER ones.
        // SecondarilySimplified renders identically to Unsegmented (a smoothed-over descendant).
        int segRingCount = segmentation == 1 ? 5 + rng.Next(0, 4) : segmentation == 2 ? 2 + rng.Next(0, 2) : 0;
        float segRingAmp = segmentation == 1 ? 0.05f : segmentation == 2 ? 0.08f : 0f;

        // appearance-generation-spec §2.2 M10: integument elaboration texture. Chitin/ShellExternal/
        // Crystalline already mirror BodyPlanType's own rig treatment above, so only the two
        // genuinely-independent values need their own pass here. AgentController.IntegumentType:
        // 1=Scales 3=FilamentsFur.
        float scaleFreq = 5f + 3f * R();
        Vector3 scaleOffset = new Vector3(R() * 100f, R() * 100f, R() * 100f);
        float furFreq = 8f + 4f * R();
        Vector3 furOffset = new Vector3(R() * 100f, R() * 100f, R() * 100f);

        // Appendage protrusions: once M5 differentiation has fired (pairCount > 0) the bump count
        // reflects the actual differentiated pair total; before that it falls back to the old
        // manipulation-tier proxy so existing (undifferentiated) organisms look unchanged.
        int bumpCount = pairCount > 0
            ? Mathf.Clamp(pairCount, 0, 6)
            : Mathf.Clamp(appendage + (motile ? 1 : 0), 0, 4);
        var bumpDirs = new Vector3[bumpCount];
        for (int i = 0; i < bumpCount; i++)
            bumpDirs[i] = new Vector3(R() * 2f - 1f, R() * 2f - 1f, R() * 2f - 1f).normalized;
        float bumpStrength = 0.18f + 0.14f * R();

        // Mixed-armor: a coarse, low-frequency second noise layer picks out irregular PATCHES of
        // extra-hard plating over the soft/endo base, rather than uniformly hardening the whole
        // surface — "dermal ossification layered over an existing exoskeleton," not a second
        // full shell (appearance-generation-spec §2.7).
        float patchFreq = 0.9f + 0.5f * R();
        Vector3 patchOffset = new Vector3(R() * 100f, R() * 100f, R() * 100f);

        // appearance-generation-spec §3.3: gradual network-foreshadow nodules — small budding
        // protrusions that grow more numerous/pronounced as networkForeshadowBucket rises (0-10),
        // giving a Distributed-architecture lineage a legibly "incipient colonial" surface reading
        // well before (or even without ever reaching) the hard ColonialModular symmetry switch.
        // Distinct frequency/threshold from the M10 Scales pass above so the two never look alike.
        float nodFrac = Mathf.Clamp01(networkForeshadowBucket / 10f); // 0 = no nodules at all, unchanged from before this axis existed
        float nodFreq = 3f + 2f * R();
        Vector3 nodOffset = new Vector3(R() * 100f, R() * 100f, R() * 100f);
        float nodThreshold = 1f - nodFrac * 0.5f; // more of the surface qualifies as the bucket rises
        float nodAmp = 0.05f + 0.05f * nodFrac;   // nodules also get more pronounced

        Vector3[] outV = new Vector3[_unitVerts.Length];
        for (int i = 0; i < _unitVerts.Length; i++)
        {
            Vector3 dir = _unitVerts[i];

            // Organelle-cluster / membrane-fold noise along the surface normal.
            float n = Perlin3(dir * noiseFreq + noiseOffset);
            float localNoiseAmp = noiseAmp;
            if (patchyArmor)
            {
                float patch = Perlin3(dir * patchFreq + patchOffset);
                if (patch > 0.55f) localNoiseAmp = 0.06f; // inside a plate patch — hard, smooth
            }
            float radial = 1f + (n - 0.5f) * 2f * localNoiseAmp;

            // Radial ridges (only meaningful for radial body plans) — sea-anemone-like fluting.
            if (ridgeAmp > 0f)
                radial += Mathf.Sin(Mathf.Atan2(dir.z, dir.x) * ridgeCount) * ridgeAmp * (1f - Mathf.Abs(dir.y));

            // M2 segmentation rings along the main body axis.
            if (segRingCount > 0)
                radial += Mathf.Sin(dir.z * segRingCount * Mathf.PI) * segRingAmp;

            // §3.3 network-foreshadow nodules — sparse budding protrusions, growing more numerous
            // and pronounced as networkForeshadowBucket rises. Independent of (and additive with)
            // every other surface treatment above, so it reads as an emerging trait layered on top
            // of whatever body plan/integument this lineage already has, not a replacement for it.
            if (nodFrac > 0f)
            {
                float nod = Perlin3(dir * nodFreq + nodOffset);
                if (nod > nodThreshold) radial += nodAmp;
            }

            // M10 integument texture — fine bumpy scales, or a fuzzy fur-like high-frequency jitter.
            if (integument == 1) // Scales
                radial += (Perlin3(dir * scaleFreq + scaleOffset) - 0.5f) * 0.035f;
            else if (integument == 3) // FilamentsFur
                radial += (Perlin3(dir * furFreq + furOffset) - 0.5f) * 0.09f;

            Vector3 p = Vector3.Scale(dir, axisScale) * (BaseRadius * radial);

            // Appendage bumps: pull vertices near a bump direction outward.
            for (int b = 0; b < bumpCount; b++)
            {
                float d = Vector3.Dot(dir, bumpDirs[b]);
                if (d > 0.82f)
                    p += dir * (bumpStrength * BaseRadius * (d - 0.82f) / 0.18f);
            }

            // Motility tail: taper/extend the rear pole (-z) into a flagellar tail.
            if (motile && dir.z < -0.55f)
                p.z -= ((-dir.z) - 0.55f) * 0.9f * BaseRadius;

            outV[i] = p;
        }

        // Crystalline: faceted geometry, non-organic surface normals (§2.7) — a fundamentally
        // different mesh topology (duplicated per-face vertices) from the shared-vertex smooth
        // meshes every other body plan uses, so it's built as a distinct pass rather than a tweak.
        if (structure == 7)
            return BuildFaceted(outV, _baseTris, lineageSeed, sym);

        Mesh mesh = new Mesh { name = $"morph_L{lineageSeed}_{sym}" };
        mesh.vertices = outV;
        mesh.triangles = _baseTris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// Duplicates one vertex per triangle corner so RecalculateNormals produces a true flat normal
    /// per face instead of the smooth vertex-averaged normals every other body plan gets — the
    /// "distinct refractive shader pass" the spec asks for starts from genuinely faceted geometry,
    /// not a smoothed mesh with a different material.
    private static Mesh BuildFaceted(Vector3[] positions, int[] tris, int lineageSeed, Symmetry sym)
    {
        var flatV = new Vector3[tris.Length];
        var flatTris = new int[tris.Length];
        for (int i = 0; i < tris.Length; i++)
        {
            flatV[i] = positions[tris[i]];
            flatTris[i] = i;
        }
        Mesh mesh = new Mesh { name = $"morph_crystalline_L{lineageSeed}_{sym}" };
        mesh.vertices = flatV;
        mesh.triangles = flatTris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// 3D value noise in [0,1] built from three 2D Perlin lookups (cheap, deterministic, seamless
    /// enough for surface displacement).
    private static float Perlin3(Vector3 p)
    {
        float xy = Mathf.PerlinNoise(p.x, p.y);
        float yz = Mathf.PerlinNoise(p.y, p.z);
        float xz = Mathf.PerlinNoise(p.x, p.z);
        return (xy + yz + xz) / 3f;
    }

    private static int HashKey(params int[] vals)
    {
        unchecked
        {
            int h = 17;
            foreach (int v in vals) h = h * 31 + v;
            return h;
        }
    }

    // ── Colonial/modular growth (appearance-generation-spec §2.6/§4.4) ────────────────────────
    // ColonialModular lineages (M1) are non-bilaterian by construction — a single deformed rig
    // can't represent them, so this is a genuinely different generator method (graph/L-system
    // growth), not a parametric variant. A seeded branching walk grows a small cluster of linked
    // "zooid" modules (each a small deformed icosphere reusing the same base geometry as every
    // other body plan), matching the spec's "no hard cutover — blends toward parametric/skeletal as
    // more axes populate": at Era 1 scale this already IS the Distributed-architecture Era 3
    // generator family in miniature (§4.3's "growth-algorithm network"), just with far fewer nodes.
    private static Mesh BuildColonial(int lineageSeed, int structure, int integument)
    {
        var rng = new System.Random(HashKey(lineageSeed, (int)Symmetry.ColonialModular, structure, integument));
        float R() => (float)rng.NextDouble();

        int moduleCount = 3 + rng.Next(0, 5); // 3-7 zooids
        float moduleRadius = BaseRadius * 0.45f;

        var positions = new List<Vector3> { Vector3.zero };
        var parents = new List<int> { -1 };
        for (int i = 1; i < moduleCount; i++)
        {
            int parent = rng.Next(0, positions.Count); // attach to any existing module — branching, not a chain
            Vector3 dir = new Vector3(R() * 2f - 1f, R() * 2f - 1f, R() * 2f - 1f).normalized;
            float dist = moduleRadius * (1.5f + 0.6f * R());
            positions.Add(positions[parent] + dir * dist);
            parents.Add(parent);
        }

        // Structural body plan still modulates per-module surface roughness, same table as the
        // single-blob path, so a chitin-bodied colonial lineage still reads as chitinous.
        float noiseAmp = structure == 1 || structure == 2 ? 0.06f : structure == 4 ? 0.20f : 0.13f;

        var allV = new List<Vector3>();
        var allTris = new List<int>();
        foreach (var center in positions)
        {
            int baseIdx = allV.Count;
            float nFreq = 1.6f + 2.4f * R();
            Vector3 nOff = new Vector3(R() * 100f, R() * 100f, R() * 100f);
            for (int i = 0; i < _unitVerts.Length; i++)
            {
                Vector3 dir = _unitVerts[i];
                float n = Perlin3(dir * nFreq + nOff);
                float radial = 1f + (n - 0.5f) * 2f * noiseAmp;
                allV.Add(center + dir * (moduleRadius * radial));
            }
            foreach (int t in _baseTris) allTris.Add(baseIdx + t);
        }

        // Connective stalks: a thin stretched-and-oriented icosphere between each module and its
        // parent, so the colony reads as genuinely LINKED modules (a graph) rather than a loose
        // cluster of unrelated blobs floating near each other.
        float stalkRadius = moduleRadius * 0.18f;
        for (int i = 1; i < positions.Count; i++)
        {
            Vector3 a = positions[parents[i]];
            Vector3 b = positions[i];
            Vector3 mid = (a + b) * 0.5f;
            Vector3 dir = (b - a).sqrMagnitude > 0.0001f ? (b - a).normalized : Vector3.forward;
            float halfLen = Vector3.Distance(a, b) * 0.5f;
            Quaternion rot = Quaternion.FromToRotation(Vector3.forward, dir);

            int baseIdx = allV.Count;
            for (int v = 0; v < _unitVerts.Length; v++)
            {
                Vector3 local = Vector3.Scale(_unitVerts[v], new Vector3(stalkRadius, stalkRadius, halfLen));
                allV.Add(mid + rot * local);
            }
            foreach (int t in _baseTris) allTris.Add(baseIdx + t);
        }

        Mesh mesh = new Mesh { name = $"morph_colonial_L{lineageSeed}_{moduleCount}" };
        if (allV.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = allV.ToArray();
        mesh.triangles = allTris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ── Base icosphere ───────────────────────────────────────────────────────────

    private static void EnsureBase()
    {
        if (_unitVerts != null) return;
        BuildIcosphere(2, out var verts, out var tris); // subdivision 2 → 162 verts, smooth enough for a blob
        _unitVerts = verts.ToArray();
        _baseTris = tris.ToArray();
    }

    private static void BuildIcosphere(int subdivisions, out List<Vector3> verts, out List<int> tris)
    {
        var v = new List<Vector3>();               // local (out params can't be captured by locals)
        var midCache = new Dictionary<long, int>();
        float t = (1f + Mathf.Sqrt(5f)) / 2f;

        void AddV(Vector3 p) => v.Add(p.normalized);
        AddV(new Vector3(-1, t, 0)); AddV(new Vector3(1, t, 0)); AddV(new Vector3(-1, -t, 0)); AddV(new Vector3(1, -t, 0));
        AddV(new Vector3(0, -1, t)); AddV(new Vector3(0, 1, t)); AddV(new Vector3(0, -1, -t)); AddV(new Vector3(0, 1, -t));
        AddV(new Vector3(t, 0, -1)); AddV(new Vector3(t, 0, 1)); AddV(new Vector3(-t, 0, -1)); AddV(new Vector3(-t, 0, 1));

        var faces = new List<int[]>
        {
            new[]{0,11,5}, new[]{0,5,1}, new[]{0,1,7}, new[]{0,7,10}, new[]{0,10,11},
            new[]{1,5,9}, new[]{5,11,4}, new[]{11,10,2}, new[]{10,7,6}, new[]{7,1,8},
            new[]{3,9,4}, new[]{3,4,2}, new[]{3,2,6}, new[]{3,6,8}, new[]{3,8,9},
            new[]{4,9,5}, new[]{2,4,11}, new[]{6,2,10}, new[]{8,6,7}, new[]{9,8,1},
        };

        int Mid(int a, int b)
        {
            long key = ((long)Mathf.Min(a, b) << 32) + Mathf.Max(a, b);
            if (midCache.TryGetValue(key, out int idx)) return idx;
            Vector3 m = ((v[a] + v[b]) * 0.5f).normalized;
            v.Add(m);
            idx = v.Count - 1;
            midCache[key] = idx;
            return idx;
        }

        for (int s = 0; s < subdivisions; s++)
        {
            var next = new List<int[]>(faces.Count * 4);
            foreach (var f in faces)
            {
                int a = Mid(f[0], f[1]);
                int b = Mid(f[1], f[2]);
                int c = Mid(f[2], f[0]);
                next.Add(new[] { f[0], a, c });
                next.Add(new[] { f[1], b, a });
                next.Add(new[] { f[2], c, b });
                next.Add(new[] { a, b, c });
            }
            faces = next;
        }

        var triList = new List<int>(faces.Count * 3);
        foreach (var f in faces) { triList.Add(f[0]); triList.Add(f[1]); triList.Add(f[2]); }

        verts = v;
        tris = triList;
    }
}
