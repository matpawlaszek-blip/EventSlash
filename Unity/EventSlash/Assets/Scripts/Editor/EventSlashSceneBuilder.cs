using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace EventSlash.Editor
{
    public static class EventSlashSceneBuilder
    {
        [MenuItem("EventSlash/Build Complete Scene and Prefabs")]
        public static void BuildCompleteSceneAndPrefabs()
        {
            EnsureFolders();

            Material screenMat = CreateScreenMaterial();
            Dictionary<string, Material> fruitMats = CreateFruitMaterials();
            Dictionary<string, GameObject> fruitPrefabs = CreateFruitPrefabs(fruitMats);
            Dictionary<string, Sprite> uiSprites = GenerateFruitNinjaUISprites();

            SetupScene(screenMat, fruitPrefabs, uiSprites);

            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("[EventSlashSceneBuilder] Updated Ninja-styled UI, HUD, and Game Over window successfully!");
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder("Assets/Materials/Fruits"))
                AssetDatabase.CreateFolder("Assets/Materials", "Fruits");
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Fruits"))
                AssetDatabase.CreateFolder("Assets/Prefabs", "Fruits");
            if (!AssetDatabase.IsValidFolder("Assets/Textures"))
                AssetDatabase.CreateFolder("Assets", "Textures");
            if (!AssetDatabase.IsValidFolder("Assets/Textures/UI"))
                AssetDatabase.CreateFolder("Assets/Textures", "UI");
            if (!AssetDatabase.IsValidFolder("Assets/Fonts"))
                AssetDatabase.CreateFolder("Assets", "Fonts");
        }

        private static Sprite SaveAndLoadSprite(Texture2D tex, string assetPath)
        {
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            File.WriteAllBytes(assetPath, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            TextureImporter ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (ti != null)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static Dictionary<string, Sprite> GenerateFruitNinjaUISprites()
        {
            var sprites = new Dictionary<string, Sprite>();

            // 1. Dojo Wooden Plank Background
            int bw = 1024, bh = 512;
            Texture2D woodBg = new Texture2D(bw, bh, TextureFormat.RGBA32, false);
            Color[] woodPixels = new Color[bw * bh];

            (float x0, float y0, float dx, float dy, float len, float w)[] slashes =
            {
                (0.58f, 0.05f, 0.18f, 0.92f, 0.95f, 0.0045f),
                (0.45f, 0.55f, 0.92f, 0.25f, 0.52f, 0.0040f),
                (0.28f, 0.42f, 0.85f, -0.45f, 0.48f, 0.0038f),
                (0.60f, 0.72f, 0.88f, -0.36f, 0.42f, 0.0042f),
                (0.32f, 0.02f, 0.35f, 0.90f, 0.45f, 0.0045f),
                (0.08f, 0.25f, 0.94f, 0.30f, 0.36f, 0.0035f)
            };

            for (int y = 0; y < bh; y++)
            {
                float ny = y / (float)bh;
                for (int x = 0; x < bw; x++)
                {
                    float nx = x / (float)bw;

                    float plankCoord = (nx + (ny - 0.5f) * 0.04f) * 9.5f;
                    int plankIdx = Mathf.FloorToInt(plankCoord);
                    float plankFrac = plankCoord - plankIdx;

                    float grain1 = Mathf.PerlinNoise(nx * 6.0f + plankIdx * 3.7f, ny * 42.0f);
                    float grain2 = Mathf.PerlinNoise(nx * 28.0f + plankIdx * 1.9f, ny * 140.0f);
                    float ringGrain = Mathf.Sin((ny * 12.0f + grain1 * 2.5f) * Mathf.PI);

                    float baseTone = 0.52f + 0.24f * grain1 + 0.12f * grain2 + 0.08f * ringGrain;
                    float plankVar = 0.88f + 0.22f * Mathf.Abs(Mathf.Sin(plankIdx * 2.13f));
                    baseTone *= plankVar;

                    Color c = new Color(0.44f * baseTone, 0.23f * baseTone, 0.08f * baseTone, 1.0f);

                    float grooveDist = Mathf.Min(plankFrac, 1.0f - plankFrac);
                    if (grooveDist < 0.035f)
                    {
                        float gFactor = grooveDist / 0.035f;
                        c *= Mathf.Lerp(0.18f, 1.0f, gFactor);
                        c.a = 1.0f;
                    }

                    float headerLine = 0.74f + nx * 0.14f;
                    if (ny > headerLine)
                    {
                        float shadowT = Mathf.Clamp01((ny - headerLine) / 0.03f);
                        c *= Mathf.Lerp(1.0f, 0.42f, shadowT);
                        c.a = 1.0f;
                    }

                    for (int s = 0; s < slashes.Length; s++)
                    {
                        var sl = slashes[s];
                        float vx = nx - sl.x0;
                        float vy = ny - sl.y0;
                        float lenNorm = Mathf.Sqrt(sl.dx * sl.dx + sl.dy * sl.dy);
                        float ux = sl.dx / lenNorm;
                        float uy = sl.dy / lenNorm;
                        float proj = vx * ux + vy * uy;
                        if (proj > 0f && proj < sl.len)
                        {
                            float perp = Mathf.Abs(-vx * uy + vy * ux);
                            float taper = Mathf.Sin(Mathf.Clamp01(proj / sl.len) * Mathf.PI);
                            float curW = sl.w * (0.35f + 0.65f * taper);
                            if (perp < curW)
                            {
                                float cutT = perp / curW;
                                c *= Mathf.Lerp(0.15f, 0.85f, cutT);
                                c.a = 1.0f;
                            }
                            else if (perp < curW * 1.8f)
                            {
                                c *= 1.08f;
                                c.a = 1.0f;
                            }
                        }
                    }

                    float distCenter = Vector2.Distance(new Vector2(nx, ny), new Vector2(0.50f, 0.42f));
                    float vignette = Mathf.Clamp(1.18f - distCenter * 0.88f, 0.35f, 1.15f);
                    c.r = Mathf.Clamp01(c.r * vignette);
                    c.g = Mathf.Clamp01(c.g * vignette);
                    c.b = Mathf.Clamp01(c.b * vignette);
                    c.a = 1.0f;

                    woodPixels[y * bw + x] = c;
                }
            }
            woodBg.SetPixels(woodPixels);
            woodBg.Apply();
            sprites["DojoWoodBg"] = SaveAndLoadSprite(woodBg, "Assets/Textures/UI/DojoWoodBg.png");

            // 2. Glowing Neon Mode Rings (Blue & Orange)
            sprites["NeonRing_Blue"] = SaveAndLoadSprite(
                CreateNeonRingTexture(512, new Color(0.05f, 0.52f, 1.0f), new Color(0.45f, 0.88f, 1.0f)),
                "Assets/Textures/UI/NeonRing_Blue.png");

            sprites["NeonRing_Orange"] = SaveAndLoadSprite(
                CreateNeonRingTexture(512, new Color(1.0f, 0.56f, 0.04f), new Color(1.0f, 0.92f, 0.35f)),
                "Assets/Textures/UI/NeonRing_Orange.png");

            // 3. Wooden Profile Plaque, Dojo Scroll Summary Board & Circular Ninja Emblem
            sprites["WoodBanner"] = SaveAndLoadSprite(CreateWoodBannerTexture(512, 128), "Assets/Textures/UI/WoodBanner.png");
            sprites["DojoScrollBoard"] = SaveAndLoadSprite(CreateDojoScrollBoardTexture(768, 512), "Assets/Textures/UI/DojoScrollBoard.png");
            sprites["NinjaBadge"] = SaveAndLoadSprite(CreateNinjaBadgeTexture(160), "Assets/Textures/UI/NinjaBadge.png");

            // 4. Ninja Sumi-e Ink Brush + Katana Slash Banner & Sliced Watermelon Score Icon
            sprites["NinjaInkBrush"] = SaveAndLoadSprite(CreateNinjaInkBrushTexture(512, 160), "Assets/Textures/UI/NinjaInkBrush.png");
            sprites["ScoreWatermelonIcon"] = SaveAndLoadSprite(CreateWatermelonScoreIconTexture(160), "Assets/Textures/UI/ScoreWatermelonIcon.png");

            // 5. Glossy Top-Right Square Icon Buttons
            sprites["IconBtn_Green"] = SaveAndLoadSprite(CreateGlossyIconBtnTexture(128, new Color(0.22f, 0.76f, 0.14f), false), "Assets/Textures/UI/IconBtn_Green.png");
            sprites["IconBtn_Red"] = SaveAndLoadSprite(CreateGlossyIconBtnTexture(128, new Color(0.86f, 0.18f, 0.18f), true), "Assets/Textures/UI/IconBtn_Red.png");

            return sprites;
        }

        private static Texture2D CreateNinjaInkBrushTexture(int w, int h)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color[] cols = new Color[w * h];

            for (int y = 0; y < h; y++)
            {
                float ny = y / (float)(h - 1);
                float dy = ny * 2f - 1f;
                for (int x = 0; x < w; x++)
                {
                    float nx = x / (float)(w - 1);
                    float dx = nx * 2f - 1f;

                    // Brush stroke taper & organic ink splatter edges
                    float noise = Mathf.PerlinNoise(nx * 14f, ny * 22f) * 0.35f
                                + Mathf.PerlinNoise(nx * 38f, ny * 45f) * 0.18f;
                    float horizFade = Mathf.Sin(Mathf.Clamp01(nx) * Mathf.PI);
                    float brushHalfHeight = 0.62f * Mathf.Pow(horizFade, 0.45f) + (noise - 0.25f) * 0.38f;

                    float absDy = Mathf.Abs(dy);
                    Color c = Color.clear;

                    if (absDy < brushHalfHeight)
                    {
                        float edgeAlpha = Mathf.Clamp01((brushHalfHeight - absDy) / 0.16f);
                        // Deep sumi-e ink black with a warm crimson-amber rim
                        Color inkCore = new Color(0.06f, 0.03f, 0.02f, 0.88f);
                        Color inkRim = new Color(0.45f, 0.10f, 0.04f, 0.82f);
                        c = Color.Lerp(inkRim, inkCore, Mathf.Clamp01((brushHalfHeight - absDy) / 0.28f));
                        c.a *= edgeAlpha;
                    }

                    // Sharp golden-white Katana Slash streak cutting across the brush banner!
                    float slashY = -0.05f + dx * 0.12f;
                    float distSlash = Mathf.Abs(dy - slashY);
                    float slashWidth = 0.065f * Mathf.Sin(Mathf.Clamp01((nx - 0.04f) / 0.92f) * Mathf.PI);
                    if (nx > 0.04f && nx < 0.96f && distSlash < slashWidth * 2.4f)
                    {
                        float glow = Mathf.Exp(-distSlash / Mathf.Max(0.005f, slashWidth * 0.7f));
                        Color bladeCol = Color.Lerp(new Color(1.0f, 0.48f, 0.08f, 0.9f), new Color(1.0f, 0.96f, 0.72f, 1.0f), glow);
                        c = Color.Lerp(c, bladeCol, glow * 0.75f);
                        c.a = Mathf.Max(c.a, glow * 0.85f);
                    }

                    cols[y * w + x] = c;
                }
            }
            tex.SetPixels(cols);
            tex.Apply();
            return tex;
        }

        private static Texture2D CreateWatermelonScoreIconTexture(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] cols = new Color[size * size];

            // Draw a tilted half-moon watermelon slice icon (like the Fruit Ninja top-left score icon!)
            float tiltRad = -0.42f;
            float cosA = Mathf.Cos(tiltRad);
            float sinA = Mathf.Sin(tiltRad);

            for (int y = 0; y < size; y++)
            {
                float py = (y / (float)(size - 1)) * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float px = (x / (float)(size - 1)) * 2f - 1f;

                    // Rotate into slice space
                    float rx = px * cosA - py * sinA;
                    float ry = px * sinA + py * cosA - 0.12f;

                    float r = Mathf.Sqrt(rx * rx + ry * ry);
                    Color c = Color.clear;

                    // Half-circle wedge: ry <= 0.12 and r <= 0.78
                    if (ry <= 0.14f && r <= 0.84f)
                    {
                        if (r > 0.76f || ry > 0.08f)
                        {
                            // Dark outline around the icon
                            c = new Color(0.12f, 0.05f, 0.02f, 1.0f);
                        }
                        else if (r > 0.65f)
                        {
                            // Outer green striped rind
                            float stripe = Mathf.Sin(Mathf.Atan2(ry, rx) * 14f);
                            c = stripe > 0f
                                ? new Color(0.18f, 0.72f, 0.16f, 1.0f)
                                : new Color(0.06f, 0.38f, 0.08f, 1.0f);
                        }
                        else if (r > 0.57f)
                        {
                            // White-green inner rind rim
                            c = new Color(0.88f, 0.96f, 0.82f, 1.0f);
                        }
                        else
                        {
                            // Juicy ruby-red watermelon flesh
                            c = new Color(0.95f, 0.14f, 0.18f, 1.0f);
                            // Black seeds arranged in an arc
                            for (int s = -2; s <= 2; s++)
                            {
                                float sAng = -Mathf.PI * 0.5f + s * 0.42f;
                                Vector2 seedPos = new Vector2(Mathf.Cos(sAng) * 0.34f, Mathf.Sin(sAng) * 0.34f);
                                if (Vector2.Distance(new Vector2(rx, ry), seedPos) < 0.048f)
                                {
                                    c = new Color(0.10f, 0.08f, 0.08f, 1.0f);
                                }
                            }
                        }
                    }

                    cols[y * size + x] = c;
                }
            }

            tex.SetPixels(cols);
            tex.Apply();
            return tex;
        }

        private static Texture2D CreateDojoScrollBoardTexture(int w, int h)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color[] cols = new Color[w * h];

            for (int y = 0; y < h; y++)
            {
                float ny = y / (float)(h - 1);
                for (int x = 0; x < w; x++)
                {
                    float nx = x / (float)(w - 1);

                    float rx = Mathf.Max(0f, Mathf.Abs(nx - 0.5f) - 0.44f) / 0.05f;
                    float ry = Mathf.Max(0f, Mathf.Abs(ny - 0.5f) - 0.42f) / 0.07f;
                    float dist = Mathf.Sqrt(rx * rx + ry * ry);
                    if (dist > 1.0f)
                    {
                        cols[y * w + x] = Color.clear;
                        continue;
                    }

                    float grain = Mathf.PerlinNoise(nx * 12f, ny * 24f);
                    float plank = Mathf.Sin(ny * 18f + grain * 2f) * 0.06f;

                    // Rich dark dojo wood interior with golden-bronze carved frame
                    Color c = new Color(0.26f + plank, 0.14f + plank * 0.5f, 0.06f, 0.97f) * (0.88f + 0.24f * grain);
                    c.a = 0.97f;

                    if (dist > 0.78f)
                    {
                        // Outer dark iron-wood border
                        c = new Color(0.12f, 0.05f, 0.02f, 1.0f);
                    }
                    else if (dist > 0.58f)
                    {
                        // Golden-bronze inner frame bevel
                        c = new Color(0.82f, 0.56f, 0.22f, 1.0f);
                    }

                    // Horizontal katana divider line under header
                    if (Mathf.Abs(ny - 0.73f) < 0.008f && nx > 0.12f && nx < 0.88f)
                    {
                        c = new Color(0.92f, 0.68f, 0.26f, 0.9f);
                    }

                    cols[y * w + x] = c;
                }
            }
            tex.SetPixels(cols);
            tex.Apply();
            return tex;
        }

        private static Texture2D CreateNeonRingTexture(int size, Color mainColor, Color coreHighlight)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] cols = new Color[size * size];

            float innerRim = 0.55f;
            float outerRim = 0.82f;

            for (int y = 0; y < size; y++)
            {
                float dy = (y / (float)(size - 1)) * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float dx = (x / (float)(size - 1)) * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    Color c = Color.clear;

                    if (r < innerRim)
                    {
                        float edgeDist = innerRim - r;
                        float innerGlow = Mathf.Exp(-edgeDist * 18.0f);
                        Color darkInterior = new Color(0.05f, 0.03f, 0.02f, 0.52f);
                        c = Color.Lerp(darkInterior, mainColor, innerGlow * 0.65f);
                        c.a = Mathf.Lerp(0.48f, 0.88f, innerGlow);
                    }
                    else if (r <= outerRim)
                    {
                        float distInner = Mathf.Abs(r - innerRim);
                        float distOuter = Mathf.Abs(r - outerRim);
                        float rimIntensity = Mathf.Max(
                            Mathf.Exp(-distInner * 42.0f),
                            Mathf.Exp(-distOuter * 42.0f)
                        );

                        Color bandColor = mainColor * 0.72f;
                        c = Color.Lerp(bandColor, coreHighlight, rimIntensity);
                        c.a = Mathf.Lerp(0.84f, 1.0f, rimIntensity);
                    }
                    else if (r < 0.99f)
                    {
                        float bloomT = (r - outerRim) / (0.99f - outerRim);
                        float glow = Mathf.Pow(1.0f - bloomT, 2.4f);
                        c = Color.Lerp(mainColor, coreHighlight, glow * 0.5f);
                        c.a = glow * 0.92f;
                    }

                    cols[y * size + x] = c;
                }
            }

            tex.SetPixels(cols);
            tex.Apply();
            return tex;
        }

        private static Texture2D CreateWoodBannerTexture(int w, int h)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color[] cols = new Color[w * h];

            for (int y = 0; y < h; y++)
            {
                float ny = y / (float)(h - 1);
                for (int x = 0; x < w; x++)
                {
                    float nx = x / (float)(w - 1);

                    float rx = Mathf.Max(0f, Mathf.Abs(nx - 0.5f) - 0.44f) / 0.06f;
                    float ry = Mathf.Max(0f, Mathf.Abs(ny - 0.5f) - 0.32f) / 0.18f;
                    float dist = Mathf.Sqrt(rx * rx + ry * ry);

                    if (dist > 1.0f)
                    {
                        cols[y * w + x] = Color.clear;
                        continue;
                    }

                    float grain = Mathf.PerlinNoise(nx * 14f, ny * 6f);
                    float grad = Mathf.Lerp(0.65f, 1.12f, ny);
                    Color wood = new Color(0.58f * grad, 0.34f * grad, 0.14f * grad, 1f) * (0.88f + 0.22f * grain);
                    wood.a = 1f;

                    if (dist > 0.78f)
                    {
                        wood = new Color(0.18f, 0.09f, 0.03f, 1f);
                    }
                    else if (dist > 0.65f)
                    {
                        wood = new Color(0.76f, 0.50f, 0.24f, 1f);
                    }

                    cols[y * w + x] = wood;
                }
            }
            tex.SetPixels(cols);
            tex.Apply();
            return tex;
        }

        private static Texture2D CreateNinjaBadgeTexture(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] cols = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                float dy = (y / (float)(size - 1)) * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float dx = (x / (float)(size - 1)) * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (r > 0.98f)
                    {
                        cols[y * size + x] = Color.clear;
                        continue;
                    }

                    Color c = new Color(0.84f, 0.62f, 0.32f, 1f);
                    if (r > 0.85f)
                        c = new Color(0.32f, 0.18f, 0.07f, 1f);
                    else if (r > 0.78f)
                        c = new Color(0.95f, 0.76f, 0.44f, 1f);

                    float headDist = Vector2.Distance(new Vector2(dx, dy), new Vector2(0.05f, 0.12f));
                    float neckDist = Vector2.Distance(new Vector2(dx, dy * 0.7f), new Vector2(0.05f, -0.45f));
                    bool isSilhouette = (headDist < 0.36f) || (dy < -0.1f && dy > -0.78f && neckDist < 0.34f);

                    if (dx < -0.22f && dx > -0.68f && Mathf.Abs(dy - (0.18f + (dx + 0.22f) * 0.45f)) < 0.09f)
                        isSilhouette = true;
                    if (dx < -0.22f && dx > -0.64f && Mathf.Abs(dy - (0.04f - (dx + 0.22f) * 0.35f)) < 0.08f)
                        isSilhouette = true;

                    if (isSilhouette && r <= 0.78f)
                    {
                        c = new Color(0.24f, 0.12f, 0.04f, 1f);
                        if (dy > 0.10f && dy < 0.22f && dx > -0.12f && dx < 0.32f)
                        {
                            c = new Color(0.95f, 0.76f, 0.44f, 1f);
                        }
                    }

                    cols[y * size + x] = c;
                }
            }
            tex.SetPixels(cols);
            tex.Apply();
            return tex;
        }

        private static Texture2D CreateGlossyIconBtnTexture(int size, Color baseCol, bool drawGear)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] cols = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                float ny = y / (float)(size - 1);
                float dy = ny * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float nx = x / (float)(size - 1);
                    float dx = nx * 2f - 1f;

                    float rx = Mathf.Max(0f, Mathf.Abs(dx) - 0.68f) / 0.28f;
                    float ry = Mathf.Max(0f, Mathf.Abs(dy) - 0.68f) / 0.28f;
                    float dist = Mathf.Sqrt(rx * rx + ry * ry);
                    if (dist > 1.0f)
                    {
                        cols[y * size + x] = Color.clear;
                        continue;
                    }

                    Color c = baseCol * Mathf.Lerp(0.72f, 1.18f, ny);
                    if (ny > 0.55f && dist < 0.82f)
                    {
                        c = Color.Lerp(c, Color.white, 0.22f);
                    }
                    c.a = 1f;

                    if (dist > 0.82f)
                    {
                        c = Color.Lerp(baseCol * 0.35f, Color.black, 0.4f);
                        c.a = 1f;
                    }

                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (drawGear)
                    {
                        float ang = Mathf.Atan2(dy, dx);
                        float teeth = Mathf.Cos(ang * 8f);
                        float outerR = 0.46f + (teeth > 0.2f ? 0.11f : 0f);
                        if (r > 0.20f && r < outerR)
                        {
                            c = new Color(0.95f, 0.96f, 0.98f, 1f);
                        }
                    }
                    else
                    {
                        if (Mathf.Abs(dx) < 0.46f && Mathf.Abs(dy - 0.05f) < 0.34f)
                        {
                            if (Mathf.Abs(dx) > 0.34f || Mathf.Abs(dy - 0.05f) > 0.22f || (Mathf.Abs(dy - 0.05f) < 0.06f && Mathf.Abs(dx) < 0.22f))
                                c = new Color(0.10f, 0.24f, 0.06f, 1f);
                        }
                    }

                    cols[y * size + x] = c;
                }
            }
            tex.SetPixels(cols);
            tex.Apply();
            return tex;
        }

        private static Material CreateScreenMaterial()
        {
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null) unlitShader = Shader.Find("Unlit/Texture");

            string screenMatPath = "Assets/Materials/Mat_EventCameraScreen.mat";
            Material screenMat = AssetDatabase.LoadAssetAtPath<Material>(screenMatPath);
            if (screenMat == null)
            {
                screenMat = new Material(unlitShader);
                AssetDatabase.CreateAsset(screenMat, screenMatPath);
            }
            screenMat.shader = unlitShader;
            screenMat.color = Color.white;
            if (screenMat.HasProperty("_BaseColor")) screenMat.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(screenMat);
            return screenMat;
        }

        private static Dictionary<string, Material> CreateFruitMaterials()
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null) litShader = Shader.Find("Standard");

            var defs = new Dictionary<string, (Color col, float smoothness)>
            {
                { "Mat_Watermelon_Rind", (new Color(0.12f, 0.48f, 0.14f), 0.65f) },
                { "Mat_Watermelon_Stripe", (new Color(0.03f, 0.18f, 0.05f), 0.65f) },
                { "Mat_Watermelon_WhiteRim", (new Color(0.85f, 0.94f, 0.80f), 0.45f) },
                { "Mat_Watermelon_Flesh", (new Color(0.88f, 0.09f, 0.15f), 0.72f) },
                { "Mat_Seed_Black", (new Color(0.05f, 0.05f, 0.05f), 0.80f) },
                { "Mat_Apple_Skin", (new Color(0.84f, 0.06f, 0.08f), 0.78f) },
                { "Mat_Apple_Flesh", (new Color(0.96f, 0.93f, 0.78f), 0.50f) },
                { "Mat_Stem_Brown", (new Color(0.28f, 0.16f, 0.07f), 0.30f) },
                { "Mat_Leaf_Green", (new Color(0.18f, 0.58f, 0.12f), 0.60f) },
                { "Mat_Orange_Peel", (new Color(0.98f, 0.42f, 0.03f), 0.55f) },
                { "Mat_Orange_WhitePith", (new Color(0.97f, 0.94f, 0.86f), 0.40f) },
                { "Mat_Orange_Flesh", (new Color(1.00f, 0.58f, 0.08f), 0.75f) },
                { "Mat_Banana_Peel", (new Color(0.96f, 0.82f, 0.08f), 0.62f) },
                { "Mat_Banana_Tip", (new Color(0.22f, 0.14f, 0.05f), 0.25f) },
                { "Mat_Banana_Flesh", (new Color(0.97f, 0.94f, 0.76f), 0.45f) },
                { "Mat_Bomb_Body", (new Color(0.12f, 0.13f, 0.16f), 0.78f) },
                { "Mat_Bomb_DangerRed", (new Color(0.92f, 0.14f, 0.09f), 0.68f) },
                { "Mat_Bomb_Cap", (new Color(0.76f, 0.56f, 0.22f), 0.75f) },
                { "Mat_Bomb_Fuse", (new Color(0.72f, 0.58f, 0.36f), 0.25f) },
                { "Mat_Bomb_Spark", (new Color(1.00f, 0.72f, 0.12f), 0.90f) }
            };

            var created = new Dictionary<string, Material>();
            foreach (var kv in defs)
            {
                string path = $"Assets/Materials/Fruits/{kv.Key}.mat";
                Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    m = new Material(litShader);
                    AssetDatabase.CreateAsset(m, path);
                }
                m.shader = litShader;
                m.color = kv.Value.col;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", kv.Value.col);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", kv.Value.smoothness);
                EditorUtility.SetDirty(m);
                created[kv.Key] = m;
            }
            return created;
        }

        private static Dictionary<string, GameObject> CreateFruitPrefabs(Dictionary<string, Material> mats)
        {
            string[] modelNames =
            {
                "Watermelon_Whole", "Watermelon_Half_A", "Watermelon_Half_B",
                "Apple_Whole", "Apple_Half_A", "Apple_Half_B",
                "Orange_Whole", "Orange_Half_A", "Orange_Half_B",
                "Banana_Whole", "Banana_Half_A", "Banana_Half_B",
                "Bomb_Whole", "Bomb_Half_A", "Bomb_Half_B"
            };

            var prefabs = new Dictionary<string, GameObject>();
            foreach (string mName in modelNames)
            {
                string fbxPath = $"Assets/Models/Fruits/{mName}.fbx";
                GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                if (fbxAsset == null) continue;

                GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
                inst.name = mName;
                MeshRenderer mr = inst.GetComponentInChildren<MeshRenderer>();
                if (mr != null)
                {
                    Material[] shared = mr.sharedMaterials;
                    for (int i = 0; i < shared.Length; i++)
                    {
                        if (shared[i] != null)
                        {
                            string cleanName = shared[i].name.Split('.')[0];
                            if (mats.TryGetValue(cleanName, out Material urpMat))
                            {
                                shared[i] = urpMat;
                            }
                        }
                    }
                    mr.sharedMaterials = shared;
                }

                string prefabPath = $"Assets/Prefabs/Fruits/{mName}.prefab";
                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
                Object.DestroyImmediate(inst);
                prefabs[mName] = savedPrefab;
            }
            return prefabs;
        }

        private static void SetupScene(Material screenMat, Dictionary<string, GameObject> prefabs, Dictionary<string, Sprite> sprites)
        {
            string[] toRemove =
            {
                "EventCameraScreen", "EventSlashController", "EventSlashCanvas",
                "MenuBackgroundCanvas", "EventSystem", "ShowcasePreviewFruits"
            };
            foreach (string objName in toRemove)
            {
                GameObject old = GameObject.Find(objName);
                while (old != null)
                {
                    Object.DestroyImmediate(old);
                    old = GameObject.Find(objName);
                }
            }

            // 1. Static Main Camera pointed at EventCameraScreen
            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                GameObject camObj = new GameObject("Main Camera");
                camObj.tag = "MainCamera";
                mainCam = camObj.AddComponent<Camera>();
                camObj.AddComponent<AudioListener>();
            }
            mainCam.transform.position = new Vector3(0f, 0f, -10f);
            mainCam.transform.rotation = Quaternion.identity;
            mainCam.orthographic = true;
            mainCam.orthographicSize = 5.0f;
            mainCam.nearClipPlane = 0.1f;
            mainCam.farClipPlane = 50.0f;
            mainCam.clearFlags = CameraClearFlags.SolidColor;
            mainCam.backgroundColor = new Color(0.84f, 0.68f, 0.46f, 1.0f);

            // 2. Directional Light
            Light dirLight = Object.FindFirstObjectByType<Light>();
            if (dirLight == null)
            {
                GameObject lightObj = new GameObject("Directional Light");
                dirLight = lightObj.AddComponent<Light>();
                dirLight.type = LightType.Directional;
            }
            dirLight.transform.position = new Vector3(0f, 5f, -5f);
            dirLight.transform.rotation = Quaternion.Euler(35f, -25f, 0f);
            dirLight.intensity = 1.4f;
            dirLight.color = new Color(1.0f, 0.98f, 0.94f);

            // 3. Full-screen EventCameraScreen Quad at Z = +5.0
            GameObject screenQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screenQuad.name = "EventCameraScreen";
            Collider col = screenQuad.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            screenQuad.transform.position = new Vector3(0f, 0f, 5.0f);
            screenQuad.transform.rotation = Quaternion.identity;
            screenQuad.transform.localScale = new Vector3(17.7778f, 10.0f, 1.0f);

            MeshRenderer screenRenderer = screenQuad.GetComponent<MeshRenderer>();
            screenRenderer.sharedMaterial = screenMat;
            screenRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            screenRenderer.receiveShadows = false;

            EventScreenFitter fitter = screenQuad.AddComponent<EventScreenFitter>();
            fitter.targetCamera = mainCam;
            fitter.distanceFromCamera = 15.0f;

            // 4. EventSlashController
            GameObject controllerObj = new GameObject("EventSlashController");
            EventCameraManager camMgr = controllerObj.AddComponent<EventCameraManager>();
            camMgr.screenRenderer = screenRenderer;
            camMgr.sensitivity = 1.0f;

            EventSlashGameManager gameMgr = controllerObj.AddComponent<EventSlashGameManager>();
            gameMgr.fruitTypes = new List<FruitDefinition>
            {
                new FruitDefinition
                {
                    name = "Arbuz",
                    wholePrefab = prefabs.GetValueOrDefault("Watermelon_Whole"),
                    halfAPrefab = prefabs.GetValueOrDefault("Watermelon_Half_A"),
                    halfBPrefab = prefabs.GetValueOrDefault("Watermelon_Half_B"),
                    juiceColor = new Color(0.92f, 0.12f, 0.18f),
                    scale = 1.25f
                },
                new FruitDefinition
                {
                    name = "Jabłko",
                    wholePrefab = prefabs.GetValueOrDefault("Apple_Whole"),
                    halfAPrefab = prefabs.GetValueOrDefault("Apple_Half_A"),
                    halfBPrefab = prefabs.GetValueOrDefault("Apple_Half_B"),
                    juiceColor = new Color(0.88f, 0.10f, 0.12f),
                    scale = 1.35f
                },
                new FruitDefinition
                {
                    name = "Pomarańcza",
                    wholePrefab = prefabs.GetValueOrDefault("Orange_Whole"),
                    halfAPrefab = prefabs.GetValueOrDefault("Orange_Half_A"),
                    halfBPrefab = prefabs.GetValueOrDefault("Orange_Half_B"),
                    juiceColor = new Color(1.00f, 0.55f, 0.05f),
                    scale = 1.35f
                },
                new FruitDefinition
                {
                    name = "Banan",
                    wholePrefab = prefabs.GetValueOrDefault("Banana_Whole"),
                    halfAPrefab = prefabs.GetValueOrDefault("Banana_Half_A"),
                    halfBPrefab = prefabs.GetValueOrDefault("Banana_Half_B"),
                    juiceColor = new Color(0.98f, 0.88f, 0.20f),
                    scale = 1.45f
                }
            };

            gameMgr.bombDefinition = new FruitDefinition
            {
                name = "Bomba",
                wholePrefab = prefabs.GetValueOrDefault("Bomb_Whole"),
                halfAPrefab = prefabs.GetValueOrDefault("Bomb_Half_A"),
                halfBPrefab = prefabs.GetValueOrDefault("Bomb_Half_B"),
                juiceColor = new Color(1.00f, 0.48f, 0.08f),
                scale = 1.35f
            };
            gameMgr.bombSpawnChance = 0.18f;

            SlashTrailVisualizer trailVis = controllerObj.AddComponent<SlashTrailVisualizer>();
            trailVis.targetCamera = mainCam;
            trailVis.planeZ = -9.2f;

            // 5. EventSystem
            GameObject esObj = new GameObject("EventSystem");
            esObj.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            esObj.AddComponent<InputSystemUIInputModule>();
#else
            esObj.AddComponent<StandaloneInputModule>();
#endif

            BuildFruitNinjaUI(gameMgr, mainCam, prefabs, sprites);

            string scenePath = EditorSceneManager.GetActiveScene().path;
            if (!string.IsNullOrEmpty(scenePath))
            {
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
            }
        }

        private static void BuildFruitNinjaUI(
            EventSlashGameManager gameMgr,
            Camera mainCam,
            Dictionary<string, GameObject> prefabs,
            Dictionary<string, Sprite> sprites)
        {
            Font ninjaImpact = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NinjaImpact.ttf");
            Font ninjaComic = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NinjaComicBold.ttf");
            Font fallbackFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (ninjaImpact == null) ninjaImpact = fallbackFont;
            if (ninjaComic == null) ninjaComic = ninjaImpact;

            // A) Menu Background Canvas at planeDistance = 12.0f (Z = +2.0f)
            GameObject bgCanvasObj = new GameObject("MenuBackgroundCanvas");
            Canvas bgCanvas = bgCanvasObj.AddComponent<Canvas>();
            bgCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            bgCanvas.worldCamera = mainCam;
            bgCanvas.planeDistance = 12.0f;
            bgCanvas.sortingOrder = -5;

            CanvasScaler bgScaler = bgCanvasObj.AddComponent<CanvasScaler>();
            bgScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            bgScaler.referenceResolution = new Vector2(1920f, 1080f);
            bgScaler.matchWidthOrHeight = 0.5f;

            CreateImagePanel(bgCanvasObj.transform, "DojoWoodBackground",
                sprites.GetValueOrDefault("DojoWoodBg"), Color.white,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, false);

            gameMgr.menuBackgroundCanvasObj = bgCanvasObj;

            // B) Foreground UI Canvas at planeDistance = 2.0f (Z = -8.0f)
            GameObject canvasObj = new GameObject("EventSlashCanvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = mainCam;
            canvas.planeDistance = 2.0f;
            canvas.sortingOrder = 10;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObj.AddComponent<GraphicRaycaster>();

            // ==================== 1. MAIN MENU PANEL ====================
            GameObject menuPanel = CreateImagePanel(canvasObj.transform, "MenuPanel",
                null, new Color(0f, 0f, 0f, 0f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, false);

            BuildFruitNinjaLogo(menuPanel.transform, ninjaImpact);

            // Top-Right Red Settings Gear Button ONLY (removed green button to the left!)
            Button btnSettings = CreateSpriteButton(menuPanel.transform, "Btn_ToggleSettings",
                sprites.GetValueOrDefault("IconBtn_Red"), "", ninjaImpact, 20, Color.white,
                new Vector2(855f, 445f), new Vector2(88f, 88f));

            MenuFruitSelector liveSelector = CreateFruitNinjaModeRing(
                menuPanel.transform,
                "Ring_LiveMode",
                sprites.GetValueOrDefault("NeonRing_Blue"),
                "KAMERA LIVE",
                "KAMERA LIVE",
                ninjaImpact,
                new Vector2(-340f, -25f),
                new Vector2(430f, 430f),
                prefabs.GetValueOrDefault("Watermelon_Whole"),
                prefabs.GetValueOrDefault("Watermelon_Half_A"),
                prefabs.GetValueOrDefault("Watermelon_Half_B"),
                new Color(0.92f, 0.12f, 0.18f),
                1.75f,
                -26f
            );

            MenuFruitSelector rawSelector = CreateFruitNinjaModeRing(
                menuPanel.transform,
                "Ring_OfflineRawMode",
                sprites.GetValueOrDefault("NeonRing_Orange"),
                "TRYB OFFLINE",
                "PLIK RAW",
                ninjaImpact,
                new Vector2(340f, -40f),
                new Vector2(430f, 430f),
                prefabs.GetValueOrDefault("Orange_Whole"),
                prefabs.GetValueOrDefault("Orange_Half_A"),
                prefabs.GetValueOrDefault("Orange_Half_B"),
                new Color(1.00f, 0.55f, 0.05f),
                1.95f,
                26f
            );

            // Bottom Profile Banner — displays ONLY the Ninja Record (sensitivity moved to Settings window)
            GameObject profileBanner = CreateImagePanel(menuPanel.transform, "ProfileWoodBanner",
                sprites.GetValueOrDefault("WoodBanner"), Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(20f, -385f), new Vector2(540f, 108f), false);

            CreateImagePanel(profileBanner.transform, "NinjaMedallion",
                sprites.GetValueOrDefault("NinjaBadge"), Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-245f, 0f), new Vector2(150f, 150f), false);

            Text profileBestText = CreateNinjaText(profileBanner.transform, "ProfileBestText",
                "PROFIL NINJA  •  REKORD: 0", ninjaImpact, 32, FontStyle.Normal,
                new Color(1.0f, 0.96f, 0.86f), new Color(0.18f, 0.07f, 0.02f),
                new Vector2(32f, 0f), new Vector2(450f, 64f), 3.0f);

            Text menuStatus = CreateNinjaText(menuPanel.transform, "MenuStatusText",
                "Przeciągnij ostrzem przez wybrany owoc, aby rozpocząć!",
                ninjaComic, 20, FontStyle.Bold,
                new Color(1.0f, 0.92f, 0.68f), new Color(0.12f, 0.04f, 0.01f),
                new Vector2(0f, -485f), new Vector2(1200f, 36f), 2.5f);

            // Settings Modal Window (opened by clicking the top-right Settings Gear button)
            GameObject settingsPopup = CreateImagePanel(menuPanel.transform, "SettingsPopupPanel",
                null, new Color(0.03f, 0.02f, 0.01f, 0.78f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, true);

            GameObject settingsBoard = CreateImagePanel(settingsPopup.transform, "SettingsDojoBoard",
                sprites.GetValueOrDefault("DojoScrollBoard"), Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(960f, 720f), true);

            CreateNinjaText(settingsBoard.transform, "SettingsTitle", "USTAWIENIA",
                ninjaImpact, 54, FontStyle.Normal,
                new Color(1.0f, 0.88f, 0.32f), new Color(0.18f, 0.05f, 0.01f),
                new Vector2(0f, 282f), new Vector2(640f, 70f), 4.0f);

            Button btnTabTracking = CreateSpriteButton(settingsBoard.transform, "Btn_TabTracking",
                sprites.GetValueOrDefault("WoodBanner"), "TRACKING (ŚLEDZENIE)", ninjaImpact, 24,
                new Color(1.0f, 0.90f, 0.28f), new Vector2(-190f, 205f), new Vector2(350f, 60f));

            Button btnTabGame = CreateSpriteButton(settingsBoard.transform, "Btn_TabGame",
                sprites.GetValueOrDefault("WoodBanner"), "GAME (ROZGRYWKA)", ninjaImpact, 24,
                new Color(0.82f, 0.76f, 0.68f), new Vector2(190f, 205f), new Vector2(350f, 60f));

            // --- TAB 1: TRACKING ---
            GameObject trackingTabPanel = CreateImagePanel(settingsBoard.transform, "TrackingTabPanel",
                null, Color.clear, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, false);

            Text minSpeedLabel = CreateNinjaText(trackingTabPanel.transform, "MinHandSpeedValueText",
                "MIN. PRĘDKOŚĆ RUCHU DŁONI: 155 px/s", ninjaImpact, 25, FontStyle.Normal,
                new Color(1.0f, 0.96f, 0.84f), new Color(0.16f, 0.06f, 0.02f),
                new Vector2(0f, 132f), new Vector2(760f, 36f), 2.8f);
            Slider minSpeedSlider = CreateSlider(trackingTabPanel.transform, "MinHandSpeedSlider",
                new Vector2(0f, 96f), new Vector2(600f, 30f), 80f, 320f, 155f, false);

            Text minThickLabel = CreateNinjaText(trackingTabPanel.transform, "MinHandThicknessValueText",
                "GĘSTOŚĆ DETEKCJI DŁONI: 260", ninjaImpact, 25, FontStyle.Normal,
                new Color(1.0f, 0.96f, 0.84f), new Color(0.16f, 0.06f, 0.02f),
                new Vector2(0f, 50f), new Vector2(760f, 36f), 2.8f);
            Slider minThickSlider = CreateSlider(trackingTabPanel.transform, "MinHandThicknessSlider",
                new Vector2(0f, 14f), new Vector2(600f, 30f), 140f, 480f, 260f, false);

            Text sensLabel = CreateNinjaText(trackingTabPanel.transform, "SensitivityValueText",
                "PROMIEŃ CIĘCIA DŁONI: 1.0x", ninjaImpact, 25, FontStyle.Normal,
                new Color(1.0f, 0.96f, 0.84f), new Color(0.16f, 0.06f, 0.02f),
                new Vector2(0f, -32f), new Vector2(760f, 36f), 2.8f);
            Slider sensSlider = CreateSlider(trackingTabPanel.transform, "SensitivitySlider",
                new Vector2(0f, -68f), new Vector2(600f, 30f), 0.4f, 2.0f, 1.0f, false);

            Button btnToggleMaxHands = CreateSpriteButton(trackingTabPanel.transform, "Btn_ToggleMaxHands",
                sprites.GetValueOrDefault("WoodBanner"), "MAKS. DŁONIE: <color=#65FF45>2 DŁONIE</color>",
                ninjaImpact, 22, new Color(1.0f, 0.95f, 0.82f), new Vector2(-205f, -132f), new Vector2(380f, 56f));
            Text maxHandsToggleLabel = btnToggleMaxHands.GetComponentInChildren<Text>(true);

            Button btnToggleSwayFilter = CreateSpriteButton(trackingTabPanel.transform, "Btn_ToggleSwayFilter",
                sprites.GetValueOrDefault("WoodBanner"), "FILTR CIAŁA: <color=#65FF45>WŁĄCZONY</color>",
                ninjaImpact, 22, new Color(1.0f, 0.95f, 0.82f), new Vector2(205f, -132f), new Vector2(380f, 56f));
            Text swayFilterToggleLabel = btnToggleSwayFilter.GetComponentInChildren<Text>(true);

            Button btnToggleDenoise = CreateSpriteButton(trackingTabPanel.transform, "Btn_ToggleDenoise",
                sprites.GetValueOrDefault("WoodBanner"), "REDUKCJA SZUMU: <color=#65FF45>WŁĄCZONA</color>",
                ninjaImpact, 22, new Color(1.0f, 0.95f, 0.82f), new Vector2(0f, -196f), new Vector2(460f, 56f));
            Text denoiseToggleLabel = btnToggleDenoise.GetComponentInChildren<Text>(true);

            // --- TAB 2: GAME ---
            GameObject gameTabPanel = CreateImagePanel(settingsBoard.transform, "GameTabPanel",
                null, Color.clear, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, false);

            Text spawnRateLabel = CreateNinjaText(gameTabPanel.transform, "SpawnRateValueText",
                "TEMPO WYRZUTU OWOCÓW: 1.0x", ninjaImpact, 24, FontStyle.Normal,
                new Color(1.0f, 0.96f, 0.84f), new Color(0.16f, 0.06f, 0.02f),
                new Vector2(0f, 136f), new Vector2(760f, 34f), 2.8f);
            Slider spawnRateSlider = CreateSlider(gameTabPanel.transform, "SpawnRateSlider",
                new Vector2(0f, 102f), new Vector2(600f, 28f), 0.5f, 2.0f, 1.0f, false);

            Text fruitSizeLabel = CreateNinjaText(gameTabPanel.transform, "FruitSizeValueText",
                "ROZMIAR OWOCÓW: 1.0x", ninjaImpact, 24, FontStyle.Normal,
                new Color(1.0f, 0.96f, 0.84f), new Color(0.16f, 0.06f, 0.02f),
                new Vector2(0f, 60f), new Vector2(760f, 34f), 2.8f);
            Slider fruitSizeSlider = CreateSlider(gameTabPanel.transform, "FruitSizeSlider",
                new Vector2(0f, 26f), new Vector2(600f, 28f), 0.7f, 1.5f, 1.0f, false);

            Text maxLivesLabel = CreateNinjaText(gameTabPanel.transform, "MaxLivesValueText",
                "LICZBA ŻYĆ NA START: 3 ♥", ninjaImpact, 24, FontStyle.Normal,
                new Color(1.0f, 0.96f, 0.84f), new Color(0.16f, 0.06f, 0.02f),
                new Vector2(0f, -16f), new Vector2(760f, 34f), 2.8f);
            Slider maxLivesSlider = CreateSlider(gameTabPanel.transform, "MaxLivesSlider",
                new Vector2(-65f, -50f), new Vector2(470f, 28f), 1f, 5f, 3f, true);
            Button btnToggleLives = CreateSpriteButton(gameTabPanel.transform, "Btn_ToggleLives",
                sprites.GetValueOrDefault("WoodBanner"), "[<color=#65FF45>✔</color>] ♥",
                ninjaImpact, 22, new Color(1.0f, 0.95f, 0.82f), new Vector2(240f, -50f), new Vector2(115f, 44f));
            Text livesToggleLabel = btnToggleLives.GetComponentInChildren<Text>(true);

            Text bombChanceLabel = CreateNinjaText(gameTabPanel.transform, "BombChanceValueText",
                "CZĘSTOTLIWOŚĆ BOMB: 18%", ninjaImpact, 24, FontStyle.Normal,
                new Color(1.0f, 0.96f, 0.84f), new Color(0.16f, 0.06f, 0.02f),
                new Vector2(0f, -92f), new Vector2(760f, 34f), 2.8f);
            Slider bombChanceSlider = CreateSlider(gameTabPanel.transform, "BombChanceSlider",
                new Vector2(0f, -126f), new Vector2(600f, 28f), 0.05f, 0.45f, 0.18f, false);

            Button btnToggleBombs = CreateSpriteButton(gameTabPanel.transform, "Btn_ToggleBombs",
                sprites.GetValueOrDefault("WoodBanner"), "BOMBY W GRZE: <color=#65FF45>WŁĄCZONE</color>",
                ninjaImpact, 23, new Color(1.0f, 0.95f, 0.82f), new Vector2(0f, -190f), new Vector2(480f, 56f));
            Text bombsToggleLabel = btnToggleBombs.GetComponentInChildren<Text>(true);

            gameTabPanel.SetActive(false);

            // --- FOOTER BUTTONS (DOMYŚLNE + ZAMKNIJ) ---
            Button btnResetDefaults = CreateSpriteButton(settingsBoard.transform, "Btn_ResetDefaults",
                sprites.GetValueOrDefault("WoodBanner"), "DOMYŚLNE", ninjaImpact, 28,
                new Color(1.0f, 0.82f, 0.45f), new Vector2(-195f, -275f), new Vector2(330f, 66f));

            Button btnCloseSettings = CreateSpriteButton(settingsBoard.transform, "Btn_CloseSettings",
                sprites.GetValueOrDefault("WoodBanner"), "ZAMKNIJ", ninjaImpact, 28,
                new Color(1.0f, 0.94f, 0.58f), new Vector2(195f, -275f), new Vector2(330f, 66f));

            settingsPopup.SetActive(false);

            // ==================== 2. HUD PANEL (Ninja Styled, No Mode Badge!) ====================
            GameObject hudPanel = CreateImagePanel(canvasObj.transform, "HudPanel",
                null, new Color(0f, 0f, 0f, 0f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, false);

            // Top-Left Ninja Score Widget with Sumi-e Ink Brush backing + Sliced Watermelon Icon
            GameObject scoreWidget = CreateImagePanel(hudPanel.transform, "NinjaScoreWidget",
                sprites.GetValueOrDefault("NinjaInkBrush"), new Color(1f, 1f, 1f, 0.92f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-710f, 445f), new Vector2(440f, 145f), false);

            CreateImagePanel(scoreWidget.transform, "WatermelonScoreIcon",
                sprites.GetValueOrDefault("ScoreWatermelonIcon"), Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-135f, 12f), new Vector2(112f, 112f), false);

            Text scoreTxt = CreateNinjaText(scoreWidget.transform, "ScoreText", "0",
                ninjaImpact, 92, FontStyle.Normal,
                new Color(1.0f, 0.85f, 0.16f), new Color(0.22f, 0.06f, 0.01f),
                new Vector2(35f, 18f), new Vector2(260f, 105f), 4.5f);
            scoreTxt.alignment = TextAnchor.MiddleLeft;

            Text bestScoreHudTxt = CreateNinjaText(scoreWidget.transform, "BestScoreHudText", "REKORD: 0",
                ninjaImpact, 28, FontStyle.Normal,
                new Color(1.0f, 0.68f, 0.18f), new Color(0.16f, 0.05f, 0.01f),
                new Vector2(-20f, -44f), new Vector2(320f, 42f), 3.0f);
            bestScoreHudTxt.alignment = TextAnchor.MiddleLeft;

            // Top-Right Lives + Menu Button
            GameObject livesBg = CreateImagePanel(hudPanel.transform, "NinjaLivesBrush",
                sprites.GetValueOrDefault("NinjaInkBrush"), new Color(1f, 1f, 1f, 0.85f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(545f, 465f), new Vector2(310f, 95f), false);

            Text livesTxt = CreateNinjaText(livesBg.transform, "LivesText", "<color=#65FF45>♥ ♥ ♥</color>",
                ninjaImpact, 52, FontStyle.Normal,
                Color.white, new Color(0.16f, 0.04f, 0.02f),
                Vector2.zero, new Vector2(280f, 75f), 3.5f);

            Button btnHudMenu = CreateSpriteButton(hudPanel.transform, "Btn_HudBackToMenu",
                sprites.GetValueOrDefault("WoodBanner"), "MENU [ESC]", ninjaImpact, 26,
                new Color(1.0f, 0.94f, 0.78f), new Vector2(825f, 465f), new Vector2(215f, 72f));

            // Dynamic Angled Ninja Combo & Slice Callout Banner
            GameObject comboContainer = CreateImagePanel(hudPanel.transform, "ComboBannerContainer",
                sprites.GetValueOrDefault("NinjaInkBrush"), Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 315f), new Vector2(760f, 175f), false);
            RectTransform comboRt = comboContainer.GetComponent<RectTransform>();
            comboRt.localRotation = Quaternion.Euler(0f, 0f, -6.0f);
            CanvasGroup comboCg = comboContainer.AddComponent<CanvasGroup>();
            comboCg.alpha = 0f;
            comboCg.interactable = false;
            comboCg.blocksRaycasts = false;

            Text comboHeaderTxt = CreateNinjaText(comboContainer.transform, "ComboHeaderText", "",
                ninjaImpact, 30, FontStyle.Italic,
                new Color(1.0f, 0.42f, 0.16f), new Color(0.15f, 0.03f, 0.01f),
                new Vector2(0f, 38f), new Vector2(680f, 46f), 3.0f);

            Text comboMainTxt = CreateNinjaText(comboContainer.transform, "ComboPopupText", "",
                ninjaImpact, 64, FontStyle.Italic,
                new Color(1.0f, 0.90f, 0.22f), new Color(0.28f, 0.04f, 0.01f),
                new Vector2(0f, -14f), new Vector2(720f, 82f), 4.5f);

            // ==================== 3. GAME OVER PANEL (Clean Dojo Summary Window + Exit to Menu) ====================
            GameObject gameOverPanel = CreateImagePanel(canvasObj.transform, "GameOverPanel",
                null, new Color(0.03f, 0.02f, 0.01f, 0.82f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, true);

            GameObject scrollBoard = CreateImagePanel(gameOverPanel.transform, "DojoSummaryBoard",
                sprites.GetValueOrDefault("DojoScrollBoard"), Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(820f, 560f), false);

            Text overTitleTxt = CreateNinjaText(scrollBoard.transform, "GameOverTitle", "KONIEC GRY",
                ninjaImpact, 72, FontStyle.Normal,
                new Color(1.0f, 0.34f, 0.24f), new Color(0.16f, 0.03f, 0.01f),
                new Vector2(0f, 185f), new Vector2(720f, 88f), 4.0f);

            CreateNinjaText(scrollBoard.transform, "PointsLabel", "ZDOBYTE PUNKTY",
                ninjaImpact, 30, FontStyle.Normal,
                new Color(1.0f, 0.90f, 0.72f), new Color(0.16f, 0.05f, 0.01f),
                new Vector2(0f, 85f), new Vector2(520f, 44f), 2.5f);

            CreateImagePanel(scrollBoard.transform, "GameOverMelonIcon",
                sprites.GetValueOrDefault("ScoreWatermelonIcon"), Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-115f, 2f), new Vector2(105f, 105f), false);

            Text overScoreTxt = CreateNinjaText(scrollBoard.transform, "GameOverScoreText", "0",
                ninjaImpact, 108, FontStyle.Normal,
                new Color(1.0f, 0.85f, 0.16f), new Color(0.20f, 0.05f, 0.01f),
                new Vector2(140f, 5f), new Vector2(340f, 120f), 4.5f);
            overScoreTxt.alignment = TextAnchor.MiddleLeft;

            Text overBestTxt = CreateNinjaText(scrollBoard.transform, "GameOverBestText", "NAJLEPSZY WYNIK: 0",
                ninjaImpact, 34, FontStyle.Normal,
                new Color(0.45f, 0.96f, 0.32f), new Color(0.08f, 0.18f, 0.04f),
                new Vector2(0f, -88f), new Vector2(620f, 48f), 3.0f);

            Button btnBackOver = CreateSpriteButton(scrollBoard.transform, "Btn_GameOverMenu",
                sprites.GetValueOrDefault("WoodBanner"), "POWRÓT DO MENU", ninjaImpact, 32,
                new Color(1.0f, 0.92f, 0.48f), new Vector2(0f, -185f), new Vector2(440f, 92f));

            // Wire references to EventSlashGameManager
            gameMgr.menuPanel = menuPanel;
            gameMgr.hudPanel = hudPanel;
            gameMgr.gameOverPanel = gameOverPanel;
            gameMgr.settingsPopupPanel = settingsPopup;

            gameMgr.scoreText = scoreTxt;
            gameMgr.bestScoreHudText = bestScoreHudTxt;
            gameMgr.livesText = livesTxt;

            gameMgr.comboBannerRect = comboRt;
            gameMgr.comboBannerGroup = comboCg;
            gameMgr.comboHeaderText = comboHeaderTxt;
            gameMgr.comboPopupText = comboMainTxt;

            gameMgr.gameOverTitleText = overTitleTxt;
            gameMgr.gameOverScoreText = overScoreTxt;
            gameMgr.gameOverBestText = overBestTxt;

            gameMgr.menuStatusText = menuStatus;
            gameMgr.menuProfileBestText = profileBestText;
            gameMgr.trackingTabPanel = trackingTabPanel;
            gameMgr.gameTabPanel = gameTabPanel;
            gameMgr.btnTabTracking = btnTabTracking;
            gameMgr.btnTabGame = btnTabGame;
            gameMgr.btnResetDefaults = btnResetDefaults;

            gameMgr.minHandSpeedSlider = minSpeedSlider;
            gameMgr.minHandSpeedValueText = minSpeedLabel;
            gameMgr.minHandThicknessSlider = minThickSlider;
            gameMgr.minHandThicknessValueText = minThickLabel;
            gameMgr.sensitivitySlider = sensSlider;
            gameMgr.sensitivityValueText = sensLabel;
            gameMgr.btnToggleMaxHands = btnToggleMaxHands;
            gameMgr.maxHandsToggleText = maxHandsToggleLabel;
            gameMgr.btnToggleSwayFilter = btnToggleSwayFilter;
            gameMgr.swayFilterToggleText = swayFilterToggleLabel;
            gameMgr.btnToggleDenoise = btnToggleDenoise;
            gameMgr.denoiseToggleText = denoiseToggleLabel;

            gameMgr.spawnRateSlider = spawnRateSlider;
            gameMgr.spawnRateValueText = spawnRateLabel;
            gameMgr.fruitSizeSlider = fruitSizeSlider;
            gameMgr.fruitSizeValueText = fruitSizeLabel;
            gameMgr.maxLivesSlider = maxLivesSlider;
            gameMgr.maxLivesValueText = maxLivesLabel;
            gameMgr.btnToggleLives = btnToggleLives;
            gameMgr.livesToggleText = livesToggleLabel;
            gameMgr.bombChanceSlider = bombChanceSlider;
            gameMgr.bombChanceValueText = bombChanceLabel;
            gameMgr.btnToggleBombs = btnToggleBombs;
            gameMgr.bombsToggleText = bombsToggleLabel;

            gameMgr.liveModeSelector = liveSelector;
            gameMgr.rawModeSelector = rawSelector;

            gameMgr.btnHudBackToMenu = btnHudMenu;
            gameMgr.btnGameOverMenu = btnBackOver;
            gameMgr.btnToggleSettings = btnSettings;
            gameMgr.btnCloseSettings = btnCloseSettings;

            hudPanel.SetActive(false);
            gameOverPanel.SetActive(false);
            menuPanel.SetActive(true);
            EditorUtility.SetDirty(gameMgr);
        }

        private static void BuildFruitNinjaLogo(Transform parent, Font ninjaFont)
        {
            GameObject logoRoot = new GameObject("FruitNinjaStyleLogo");
            logoRoot.transform.SetParent(parent, false);
            RectTransform rt = logoRoot.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-460f, 365f);
            rt.sizeDelta = new Vector2(920f, 220f);
            rt.localRotation = Quaternion.Euler(0f, 0f, 5.5f);

            CreateNinjaText(logoRoot.transform, "Logo_EVENT", "EVENT",
                ninjaFont, 118, FontStyle.Italic,
                new Color(1.00f, 0.85f, 0.14f), new Color(0.16f, 0.06f, 0.01f),
                new Vector2(-195f, 2f), new Vector2(460f, 160f), 4.5f);

            CreateNinjaText(logoRoot.transform, "Logo_SLASH", "SLASH",
                ninjaFont, 118, FontStyle.Italic,
                new Color(0.94f, 0.97f, 1.0f), new Color(0.08f, 0.10f, 0.14f),
                new Vector2(165f, 2f), new Vector2(460f, 160f), 4.5f);
        }

        private static MenuFruitSelector CreateFruitNinjaModeRing(
            Transform parent,
            string name,
            Sprite ringSprite,
            string topArcText,
            string bottomArcText,
            Font font,
            Vector2 anchoredPos,
            Vector2 sizeDelta,
            GameObject wholePrefab,
            GameObject halfAPrefab,
            GameObject halfBPrefab,
            Color juiceCol,
            float fruitScale,
            float rotSpeed)
        {
            GameObject container = new GameObject(name);
            container.transform.SetParent(parent, false);
            RectTransform rt = container.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            Image hitImg = container.AddComponent<Image>();
            hitImg.color = new Color(0f, 0f, 0f, 0.005f);
            hitImg.raycastTarget = true;

            GameObject ringVis = CreateImagePanel(container.transform, "RotatingRingVisual",
                ringSprite, Color.white,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, false);
            RectTransform ringVisRt = ringVis.GetComponent<RectTransform>();

            float arcRadius = sizeDelta.x * 0.342f;
            CreateCurvedArcText(ringVis.transform, "TopArc", topArcText, font, 24, arcRadius, 135f, -7.2f);
            CreateCurvedArcText(ringVis.transform, "BottomArc", bottomArcText, font, 24, arcRadius, -45f, -7.2f);

            MenuFruitSelector selector = container.AddComponent<MenuFruitSelector>();
            selector.ringRectTransform = rt;
            selector.rotatingRingGraphic = ringVisRt;
            selector.stationName = topArcText;
            selector.wholeFruitPrefab = wholePrefab;
            selector.halfAPrefab = halfAPrefab;
            selector.halfBPrefab = halfBPrefab;
            selector.juiceColor = juiceCol;
            selector.fruitScale = fruitScale;
            selector.ringRotateSpeed = rotSpeed;

            return selector;
        }

        private static void CreateCurvedArcText(
            Transform ringParent,
            string prefix,
            string text,
            Font font,
            int fontSize,
            float radius,
            float centerAngleDeg,
            float stepAngleDeg)
        {
            int n = text.Length;
            float startAngle = centerAngleDeg - (n - 1) * 0.5f * stepAngleDeg;

            for (int i = 0; i < n; i++)
            {
                char c = text[i];
                if (c == ' ') continue;

                float angDeg = startAngle + i * stepAngleDeg;
                float angRad = angDeg * Mathf.Deg2Rad;
                Vector2 pos = new Vector2(Mathf.Cos(angRad) * radius, Mathf.Sin(angRad) * radius);

                Text chTxt = CreateNinjaText(ringParent, $"{prefix}_{i}_{c}", c.ToString(),
                    font, fontSize, FontStyle.Normal, Color.white, new Color(0.06f, 0.08f, 0.14f),
                    pos, new Vector2(38f, 38f), 2.0f);
                chTxt.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angDeg - 90f);
            }
        }

        private static GameObject CreateImagePanel(
            Transform parent,
            string name,
            Sprite sprite,
            Color color,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPos,
            Vector2 sizeDelta,
            bool raycastTarget)
        {
            GameObject panel = new GameObject(name);
            panel.transform.SetParent(parent, false);
            RectTransform rt = panel.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            Image img = panel.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycastTarget;
            return panel;
        }

        private static Text CreateNinjaText(
            Transform parent,
            string name,
            string content,
            Font font,
            int fontSize,
            FontStyle style,
            Color color,
            Color outlineColor,
            Vector2 anchoredPos,
            Vector2 sizeDelta,
            float outlineThickness = 3.0f)
        {
            GameObject txtObj = new GameObject(name);
            txtObj.transform.SetParent(parent, false);
            RectTransform rt = txtObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            Text txt = txtObj.AddComponent<Text>();
            txt.font = font;
            txt.text = content;
            txt.fontSize = fontSize;
            txt.fontStyle = style;
            txt.color = color;
            txt.supportRichText = true;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.raycastTarget = false;

            Outline ol = txtObj.AddComponent<Outline>();
            ol.effectColor = new Color(outlineColor.r, outlineColor.g, outlineColor.b, 0.96f);
            ol.effectDistance = new Vector2(outlineThickness, -outlineThickness);

            Shadow sh = txtObj.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.90f);
            sh.effectDistance = new Vector2(outlineThickness * 1.45f, -outlineThickness * 1.45f);
            return txt;
        }

        private static Button CreateSpriteButton(
            Transform parent,
            string name,
            Sprite bgSprite,
            string label,
            Font font,
            int fontSize,
            Color textColor,
            Vector2 anchoredPos,
            Vector2 sizeDelta)
        {
            GameObject btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent, false);
            RectTransform rt = btnObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            Image img = btnObj.AddComponent<Image>();
            img.sprite = bgSprite;
            img.color = Color.white;
            img.raycastTarget = true;

            Button btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.0f, 0.94f, 0.78f, 1f);
            cb.pressedColor = new Color(0.78f, 0.72f, 0.65f, 1f);
            btn.colors = cb;

            if (!string.IsNullOrEmpty(label))
            {
                Text lbl = CreateNinjaText(btnObj.transform, "Label", label, font, fontSize, FontStyle.Normal,
                    textColor, new Color(0.16f, 0.05f, 0.01f), Vector2.zero, sizeDelta, 2.8f);
                lbl.raycastTarget = false;
            }
            return btn;
        }

        private static Slider CreateSlider(Transform parent, string name, Vector2 anchoredPos, Vector2 sizeDelta, float minVal, float maxVal, float defaultVal, bool wholeNumbers = false)
        {
            GameObject sliderObj = new GameObject(name);
            sliderObj.transform.SetParent(parent, false);
            RectTransform rt = sliderObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            CreateImagePanel(sliderObj.transform, "Background", null, new Color(0.20f, 0.11f, 0.04f, 1f),
                new Vector2(0f, 0.2f), new Vector2(1f, 0.8f), Vector2.zero, Vector2.zero, true);

            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderObj.transform, false);
            RectTransform fillAreaRt = fillArea.AddComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0f, 0.2f);
            fillAreaRt.anchorMax = new Vector2(1f, 0.8f);
            fillAreaRt.sizeDelta = new Vector2(-8f, 0f);

            GameObject fill = CreateImagePanel(fillArea.transform, "Fill", null, new Color(0.35f, 0.92f, 0.25f, 1f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, false);

            GameObject handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(sliderObj.transform, false);
            RectTransform handleAreaRt = handleArea.AddComponent<RectTransform>();
            handleAreaRt.anchorMin = Vector2.zero;
            handleAreaRt.anchorMax = Vector2.one;
            handleAreaRt.sizeDelta = new Vector2(-8f, 0f);

            GameObject handle = CreateImagePanel(handleArea.transform, "Handle", null, new Color(1.0f, 0.88f, 0.32f, 1f),
                new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(20f, 0f), true);

            Slider slider = sliderObj.AddComponent<Slider>();
            slider.fillRect = fill.GetComponent<RectTransform>();
            slider.handleRect = handle.GetComponent<RectTransform>();
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.wholeNumbers = wholeNumbers;
            slider.minValue = minVal;
            slider.maxValue = maxVal;
            slider.value = defaultVal;

            return slider;
        }
    }
}
