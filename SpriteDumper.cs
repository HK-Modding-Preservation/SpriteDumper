using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Modding;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace SpriteDumper;

public class SpriteDumper : Mod
{
    private readonly string _dir;

    public override string GetVersion() => "0.0.0.0";

    public SpriteDumper() : base("Sprite Dumper")
    {
        _dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "/Sprites/";

        if (!Directory.Exists(_dir)) Directory.CreateDirectory(_dir);
    }

    public override void Initialize()
    {
        InfoLog("!Initialize");

        ModHooks.HeroUpdateHook += UpdateHook;

        InfoLog("~Initialize");
    }

    private void UpdateHook()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            GameManager.instance.StartCoroutine(SpriteDumpCoroutine(Resources.FindObjectsOfTypeAll<SpriteRenderer>()));
            GameManager.instance.StartCoroutine(SpriteDumpCoroutine(UObject.FindObjectsOfType<SpriteRenderer>(true)));
            GameManager.instance.StartCoroutine(SpriteDumpCoroutine(Resources.FindObjectsOfTypeAll<Sprite>()));
            GameManager.instance.StartCoroutine(SpriteDumpCoroutine(UObject.FindObjectsOfType<Sprite>(true)));
        }
    }

    private IEnumerator SpriteDumpCoroutine(SpriteRenderer[] srs)
    {
        InfoLog("!Dumping Sprites!");
        foreach (SpriteRenderer sr in srs)
        {
            DumpSprite(sr.sprite);
            yield return null;
        }
        InfoLog("~Dumping Sprites!");
    }

    private IEnumerator SpriteDumpCoroutine(Sprite[] srs)
    {
        InfoLog("!Dumping Sprites!");
        foreach (Sprite sr in srs)
        {
            DumpSprite(sr);
            yield return null;
        }
        InfoLog("~Dumping Sprites!");
    }

    private string GetTextureHash(byte[] pngBytes)
    {
        SHA512 hash = new SHA512Managed();
        return BitConverter.ToString(hash.ComputeHash(pngBytes)).Replace("-", "");
    }

    private void DumpSprite(Sprite sprite)
    {
        if (sprite is null) return;
        DebugLog("!DumpSprite");

        //byte[] hashBytes = readTex.GetRawTextureData();
        string spriteCollectionName = sprite.name;//GetTextureHash(hashBytes);
        string folder = $"{this._dir}";
        if (sprite.packed)
        {
            // put sprite in folder
            if (!Directory.Exists($"{_dir}/{sprite.texture.name}/")) Directory.CreateDirectory($"{_dir}/{sprite.texture.name}/");
            folder = $"{_dir}/{sprite.texture.name}/";
        }
        if (!File.Exists($"{folder}/{spriteCollectionName}.png"))
        {
            Texture2D origTex = sprite.texture;
            Texture2D readTex = ExtractTextureFromSprite(sprite);
            try
            {
                byte[] pngBytes = readTex.EncodeToPNG();

                SaveTex(pngBytes, $"{folder}/{spriteCollectionName}.png");
            }
            catch (Exception)
            {
                DebugLog("---DumpSprite");
            }
            UObject.DestroyImmediate(readTex);
        }
        DebugLog("~DumpSprite");
    }

    private Texture2D ExtractTextureFromSprite(Sprite sprite)
    {
        (int width, int height) testSpriteRect = (sprite.texture.width, sprite.texture.height);
        List<Vector2Int> texUVs = new();
        List<(Vector2Int, Vector2Int, Vector2Int)> triangles = new();
        int i;
        bool[][] contents;
        float triangleArea;
        float pab, pbc, pac;
        Vector2Int p;
        int x, y;
        int minX, maxX, minY, maxY;
        int width, height;
        Texture2D origTex, outTex;

        foreach (Vector2 item in sprite.uv)
            texUVs.Add(new Vector2Int(Mathf.RoundToInt(item.x * (testSpriteRect.width - 1)), Mathf.RoundToInt(item.y * (testSpriteRect.height - 1))));
        for (i = 0; i < sprite.triangles.Length; i += 3)
            triangles.Add((texUVs[sprite.triangles[i]], texUVs[sprite.triangles[i+1]], texUVs[sprite.triangles[i+2]]));
        //DebugLog("UVs:");
        //foreach (var uv in texUVs)
        //    DebugLog($"\t({uv.x}, {uv.y})");
        //DebugLog("Triangles:");
        //foreach (var uv in triangles)
        //    DebugLog($"\t({uv.Item1.x}, {uv.Item1.y}), ({uv.Item2.x}, {uv.Item2.y}), ({uv.Item3.x}, {uv.Item3.y})");

        minX = texUVs.Select(uv => uv.x).ToList().Min();
        maxX = texUVs.Select(uv => uv.x).ToList().Max();
        minY = texUVs.Select(uv => uv.y).ToList().Min();
        maxY = texUVs.Select(uv => uv.y).ToList().Max();
        width = maxX - minX + 1;
        height = maxY - minY + 1;

        #region Make bool array of important contents

        contents = new bool[height][];
        for (i = 0; i < contents.Length; i++)
            contents[i] = new bool[width];
        foreach (var item in triangles)
        {
            triangleArea = CalcTriangleArea(item.Item1, item.Item2, item.Item3);
            for (x = 0; x < width; x++)
            {
                for (y = 0; y < height; y++)
                {
                    p = new Vector2Int(x + minX, y + minY);
                    pab = CalcTriangleArea(item.Item1, item.Item2, p);
                    pbc = CalcTriangleArea(p, item.Item2, item.Item3);
                    pac = CalcTriangleArea(item.Item1, p, item.Item3);
                    if (Math.Abs((pab + pbc + pac) - triangleArea) < 0.001f)
                        contents[y][x] = true;
                }
            }
        }

        #endregion

        origTex = MakeTextureReadable(sprite.texture);
        outTex = new Texture2D(width, height);

        for (x = 0; x < width; x++)
        {
            for (y = 0; y < height; y++)
            {
                if (!contents[y][x])
                    outTex.SetPixel(x, y, new Color(0, 0, 0, 0));
                else
                    outTex.SetPixel(x, y, origTex.GetPixel(minX + x, minY + y));
            }
        }
        outTex.Apply();

        Texture2D.DestroyImmediate(origTex);

        return RotateTextureFromSprite(outTex, sprite);
    }

    private Texture2D RotateTextureFromSprite(Texture2D tex, Sprite sprite)
    {
        SpritePackingRotation rotation = sprite.packingRotation;
        if (rotation == SpritePackingRotation.None) return tex;
        Texture2D outTex = new Texture2D(tex.width, tex.height);
        if (rotation == SpritePackingRotation.FlipHorizontal)
        {
            for (int x = 0; x < tex.width; x++)
            {
                for (int y = 0; y < tex.height; y++)
                {
                    outTex.SetPixel(x, y, tex.GetPixel(tex.width - (x + 1), y));
                }
            }
        }
        else if (rotation == SpritePackingRotation.FlipVertical)
        {
            for (int x = 0; x < tex.width; x++)
            {
                for (int y = 0; y < tex.height; y++)
                {
                    outTex.SetPixel(x, y, tex.GetPixel(x, tex.height - (y - 1)));
                }
            }
        }
        else if (rotation == SpritePackingRotation.Rotate180)
        {
            for (int x = 0; x < tex.width; x++)
            {
                for (int y = 0; y < tex.height; y++)
                {
                    outTex.SetPixel(x, y, tex.GetPixel(tex.width - (x + 1), tex.height - (y - 1)));
                }
            }
        }
        Texture2D.DestroyImmediate(tex);
        return outTex;
    }

    private static float CalcTriangleArea(Vector2Int a, Vector2Int b, Vector2Int c)
    {
        return Mathf.Abs(((a.x * (b.y - c.y)) + (b.x * (c.y - a.y)) + (c.x * (a.y - b.y))) / 2f);
    }

    private static Texture2D MakeTextureReadable(Texture2D orig)
    {
        DebugLog("!makeTextureReadable");
        Texture2D ret = new Texture2D(orig.width, orig.height);
        RenderTexture tempRt = RenderTexture.GetTemporary(orig.width, orig.height, 0);
        Graphics.Blit(orig, tempRt);
        RenderTexture tmpActiveRt = RenderTexture.active;
        RenderTexture.active = tempRt;
        ret.ReadPixels(new Rect(0f, 0f, tempRt.width, tempRt.height), 0, 0);
        ret.Apply();
        RenderTexture.active = tmpActiveRt;
        RenderTexture.ReleaseTemporary(tempRt);
        DebugLog("~makeTextureReadable");
        return ret;
    }

    private static void SaveTex(byte[] pngBytes, string filename)
    {
        DebugLog("!saveTex");
        using (FileStream fileStream2 = new FileStream(filename, FileMode.Create))
        {
            fileStream2.Write(pngBytes, 0, pngBytes.Length);
        }
        DebugLog("~saveTex");
    }

    private static void InfoLog(string msg)
    {
        Modding.Logger.Log($"[{typeof(SpriteDumper).FullName.Replace(".", "][")}] - {msg}");
        Debug.Log($"[{typeof(SpriteDumper).FullName.Replace(".", "][")}] - {msg}");
    }
    private static void InfoLog(object msg) => InfoLog($"{msg}");
    private static void DebugLog(string msg)
    {
        Modding.Logger.LogDebug($"[{typeof(SpriteDumper).FullName.Replace(".", "][")}] - {msg}");
        Debug.Log($"[{typeof(SpriteDumper).FullName.Replace(".", "][")}] - {msg}");
    }
    private static void DebugLog(object msg) => DebugLog($"{msg}");
}