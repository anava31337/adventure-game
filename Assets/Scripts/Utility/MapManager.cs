using UnityEngine;
using SimpleJSON;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine.Tilemaps;
using System;
using System.Text.RegularExpressions;
//using UnityEditor.UIElements;
using UnityEngine.SceneManagement;
public class MapManager : MonoBehaviour
{
    #region Variables
    public PhysicsMaterial2D tileMaterial;
    [SerializeField]
    public Map map;
    private TextAsset mapFile;
    private Dictionary<int, Tile> tiles = new Dictionary<int, Tile>();
    private Dictionary<int, AnimatedTile> tileAnimations = new Dictionary<int, AnimatedTile>();
    private Dictionary<int, Tilemap> tilemaps = new Dictionary<int, Tilemap>();
    private Dictionary<int, GameObject> objects = new Dictionary<int, GameObject>();
    //private Dictionary<int, GameObject> objects = new Dictionary<int, GameObject>();
    private int tileCount = 0;
    private int objectCount = 0;
    private JSONNode json = new JSONNode();
    #endregion

    #region Properties
    public int groundLayerID { get; set; }
    public int platformLayerID { get; set; } = -1;   // -1 = no platform layer in this map

    // ── Chunked loading ───────────────────────────────────────────────────────
    [Header("Chunked Loading")]
    [Tooltip("Tiles per chunk side. 16 is a good default. Larger = fewer updates but more loaded at once.")]
    public int chunkSize = 16;
    [Tooltip("Extra chunks loaded beyond what the camera can see, as a buffer so loading isn't visible. 1–2 is typical.")]
    public int chunkMargin = 1;
    [Tooltip("Seconds between chunk-visibility checks. Small but non-zero to avoid doing it every frame.")]
    public float chunkUpdateInterval = 0.15f;

    [Tooltip("The object chunks load around. Drag your player here. If left empty, " +
             "MapManager falls back to finding an object tagged 'Player'. If neither " +
             "exists, NO TILES WILL RENDER — a warning is logged saying so.")]
    public Transform playerTransform;

    // Per-cell tile record so chunks can be placed/cleared on demand without re-parsing JSON.
    private struct CellTile
    {
        public int       tileId;        // resolved tile id (already offset)
        public Matrix4x4 matrix;        // flip/rotation transform
        public bool      animated;      // true if this id maps to an AnimatedTile
    }
    // layerIndex → (cellPos → CellTile). Only tile layers appear here.
    private Dictionary<int, Dictionary<Vector3Int, CellTile>> _layerCells
        = new Dictionary<int, Dictionary<Vector3Int, CellTile>>();

    private readonly HashSet<Vector2Int> _loadedChunks = new HashSet<Vector2Int>();
    private Transform _player;
    private Camera    _cam;
    private float     _chunkTimer;
    private bool      _chunkingReady;
    public float MaxHeight { get; set; }
    public float MaxWidth { get; set; }
    public delegate void Message(string msg);
    public static event Message OnMessage;
    public static MapManager Instance { get; private set; }
    #endregion

    #region MonoBehaviour
    private void Awake()
    {
        if (!Instance)
        {
            Instance = this;
        }
        else
        {
            Destroy(this.gameObject);
        }
        //if (hasBeenRead != true)
        {
            ReadMap();
        }
        //else
        {
            //LoadMap ();
        }
    }

    private void OnEnable()
    {
        //Item.OnRemove += RemoveItemFromMap;
    }

    private void Start()
    {

    }

    private void OnDestroy()
    {
        // Free everything this MapManager built so it can be garbage-collected
        // when the scene unloads — prevents memory growing across map transitions.
        tiles.Clear();
        tileAnimations.Clear();
        tilemaps.Clear();
        objects.Clear();
        _layerCells.Clear();
        _loadedChunks.Clear();
        Resources.UnloadUnusedAssets();
    }

    private void OnDisable()
    {
        //SaveMap();
        //Item.OnRemove -= RemoveItemFromMap;
        //Debug.Log ("TEST"); //Triggers
    }
    #endregion

    #region Methods
    public void ReadMap()
    {
        mapFile = Resources.Load("Maps/" + SceneManager.GetActiveScene().name) as TextAsset;//Application.loadedLevelName) as TextAsset; //this line, line 134, line 135 eliminate the need for the SetMapData method?
        if (mapFile != null)
        {
            map = new Map(mapFile.name, mapFile.text);
            json = JSON.Parse(mapFile.text);

            uint flippedHorizontally = 0x80000000;
            uint flippedVertically = 0x40000000;
            uint flippedDiagonally = 0x20000000;
            int width;
            int height;
            int x;
            int y;
            uint tileID;
            uint objectID;
            int objectIDOffset;
            int tileIDOffset;
            float xscale;
            float yscale;
            float rotation;
            map.LayerCount = json["layers"].Count;

            for (int tilesets = 0; tilesets < json["tilesets"].Count; tilesets++)
            {
                List<Tile> tileset = new List<Tile>();
                string path;
                try
                {
                    //Load the tile path, and tiles...
                    tileIDOffset = json["tilesets"][tilesets]["firstgid"].AsInt;
                    path = json["tilesets"][tilesets]["image"].ToString().Trim('"').Remove(0, 3); //Removes the "../" at the beginning of path...
                    path = path.Remove(path.Length - 4, 4); //Removes the ".png" file extension at the end of path...
                    string[] paths = path.Split('/');
                    path = "";
                    foreach (string s in paths)
                    {
                        if (s != paths[paths.Length - 1])
                            path += s + '/';
                    }
                    path = path + paths[paths.Length - 1] + "/Tiles/";
                    tileset = Resources.LoadAll(path).OfType<Tile>().ToList();
                    tileset = tileset.OrderBy(i => Convert.ToInt16(Regex.Match(i.name, @"_[0-9]+").Captures[0].Value.Trim('_'))).ToList();
                    /*
                    try
                    {
                        //Debug.Log(path);
                        //Debug.Log(tilesets + " Successful");
                        tileset = Resources.LoadAll(path).OfType<Tile>().ToList();
                        tileset = tileset.OrderBy(i => Convert.ToInt16(Regex.Match(i.name, @"_[0-9]+").Captures[0].Value.Trim('_'))).ToList();
                        //Debug.Log(Resources.Load(path).ToString());//tileset[tileset.Count - 1].sprite.name);
                    }
                    catch (Exception e)
                    {
                        //Debug.Log(path);
                        //Debug.Log(tilesets + " Failed");
                        //Debug.Log(e);
                    }
                    */

                    //Add the loaded tiles to the tile dictionary...
                    for (int tile = 0; tile < json["tilesets"][tilesets]["tilecount"].AsInt; tile++)
                    {
                        // ColliderType.Sprite uses the sprite's physics outline (traced from opaque pixels).
                        // Transparent/near-transparent sprites produce an empty outline → no collision.
                        // ColliderType.Grid fills the entire cell rectangle regardless of sprite content.
                        // We use Grid as a fallback for any sprite Unity couldn't trace a shape from.
                        tileset[tile].colliderType =
                            (tileset[tile].sprite != null && tileset[tile].sprite.GetPhysicsShapeCount() > 0)
                            ? Tile.ColliderType.Sprite
                            : Tile.ColliderType.Grid;

                        tiles.Add((int)tile + tileIDOffset, tileset[tile]);
                    }
                    
                    //Now create and add the tile animations...
                    for (int tileanims = 0; tileanims < json["tilesets"][tilesets]["tiles"].AsArray.Count; tileanims++)
                    {
                        if (json["tilesets"][tilesets]["tiles"][tileanims]["animation"] != null)
                        {
                            List<Sprite> frames = new List<Sprite>();
                            AnimatedTile at = ScriptableObject.CreateInstance(typeof(AnimatedTile)) as AnimatedTile;
                            int animationID = json["tilesets"][tilesets]["tiles"][tileanims]["id"].AsInt + tileIDOffset;
                            int frameCount = json["tilesets"][tilesets]["tiles"][tileanims]["animation"].AsArray.Count;

                            for (int animFrames = 0; animFrames < frameCount; animFrames++)
                            {
                                tileID = json["tilesets"][tilesets]["tiles"][tileanims]["animation"][animFrames]["tileid"].AsUInt;
                                frames.Add(tiles[(int)tileID + tileIDOffset].sprite);
                            }
                            at.m_AnimatedSprites = frames.ToArray();
                            tileAnimations.Add(animationID, at);
                            tileAnimations[animationID].m_MinSpeed = 1000 / json["tilesets"][tilesets]["tiles"][tileanims]["animation"][0]["duration"].AsUInt; //json["tilesets"][tilesets]["tiles"][tileanims]["animation"][animationID]["duration"].AsUInt //14f;
                            tileAnimations[animationID].m_MaxSpeed = 1000 / json["tilesets"][tilesets]["tiles"][tileanims]["animation"][0]["duration"].AsUInt; //json["tilesets"][tilesets]["tiles"][tileanims]["animation"][animationID]["duration"].AsUInt //14f;
                            tileAnimations[animationID].m_TileColliderType = Tile.ColliderType.Sprite;
                        }
                    }
                }
                //Create the object sets and add to the object dictionary...
                catch
                {
                    if (json["tilesets"][tilesets]["grid"] != null)
                    {
                        for (int obj = 0; obj < json["tilesets"][tilesets]["tiles"].Count; obj++)
                        {
                            objectID = json["tilesets"][tilesets]["tiles"][obj]["id"].AsUInt;
                            objectIDOffset = json["tilesets"][tilesets]["firstgid"].AsInt;
                            path = json["tilesets"][tilesets]["tiles"][obj]["image"].ToString().Trim('"').Remove(0, 3);
                            path = path.Remove(path.Length - 4, 4); // Remove the .png or other file extension from the filename...
                            path = path.Remove(0, 4); // Remove the enclosing folder name "Art"...
                            path = "Prefabs/" + path; // Place "Prefabs/" before corresponding folder path
                            objects.Add((int)objectID + objectIDOffset, Resources.Load(path) as GameObject);
                            //Debug.Log(objects[(int)objectID + objectIDOffset].GetComponent<SpriteRenderer>().name); //The sprite's name...
                            //Debug.Log(objects[(int)objectID + objectIDOffset].GetComponent<SpriteRenderer>().sprite.rect.size.x); //The sprite's width...
                        }
                    }
                    else
                    {
                        //Debug.Log("ERROR");
                    }
                    if (OnMessage != null)
                    {
                        //OnMessage (e.Message.ToString ());
                    }
                }
            }

            for (int layer = 0; layer < map.LayerCount; layer++)
            {
                //Load the ground/collision layer
                if (json["layers"][layer]["name"].ToString().Trim('"') == "GROUND")
                {
                    string tint = json["layers"][layer]["tintcolor"] != null ? json["layers"][layer]["tintcolor"].ToString() : "#ffffff";
                    float alpha = json["layers"][layer]["opacity"] != null ? json["layers"][layer]["opacity"].AsFloat : 1f;

                    tilemaps.Add(layer, CreateTilemap(json["layers"][layer]["name"].ToString().Trim('"'), 0.5f, 0.5f, tint, alpha)); tilemaps[layer].gameObject.AddComponent<TilemapCollider2D>();
                    tilemaps[layer].gameObject.AddComponent<CompositeCollider2D>();
                    tilemaps[layer].gameObject.GetComponent<TilemapCollider2D>().usedByComposite = true;
                    tilemaps[layer].gameObject.GetComponent<CompositeCollider2D>().vertexDistance = 0f;
                    tilemaps[layer].gameObject.GetComponent<CompositeCollider2D>().edgeRadius = 0f;
                    tilemaps[layer].gameObject.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
                    tilemaps[layer].gameObject.GetComponent<Rigidbody2D>().sharedMaterial = this.tileMaterial;
                    tilemaps[layer].gameObject.layer = LayerMask.NameToLayer("Ground"); // 13 Foreground Layer
                    tilemaps[layer].gameObject.GetComponent<TilemapRenderer>().sortingOrder = layer;
                    groundLayerID = layer;
                }
                else if (json["layers"][layer]["name"].ToString().Trim('"') == "WATER")
                {
                    string tint = json["layers"][layer]["tintcolor"] != null ? json["layers"][layer]["tintcolor"].ToString() : "#ffffff";
                    float alpha = json["layers"][layer]["opacity"] != null ? json["layers"][layer]["opacity"].AsFloat : 1f;

                    tilemaps.Add(layer, CreateTilemap(json["layers"][layer]["name"].ToString().Trim('"'), 0.5f, 0.5f, tint, alpha)); tilemaps[layer].gameObject.AddComponent<TilemapCollider2D>();
                    tilemaps[layer].gameObject.AddComponent<Rigidbody2D>();
                    tilemaps[layer].gameObject.AddComponent<CompositeCollider2D>();
                    tilemaps[layer].gameObject.GetComponent<CompositeCollider2D>().isTrigger = true;
                    tilemaps[layer].gameObject.GetComponent<TilemapCollider2D>().isTrigger = true;
                    tilemaps[layer].gameObject.GetComponent<TilemapCollider2D>().usedByComposite = true;
                    tilemaps[layer].gameObject.GetComponent<CompositeCollider2D>().vertexDistance = 0f;
                    tilemaps[layer].gameObject.GetComponent<CompositeCollider2D>().edgeRadius = 0f;
                    tilemaps[layer].gameObject.AddComponent<TerrainVolume>();
                    tilemaps[layer].gameObject.GetComponent<TerrainVolume>().modifiers = Resources.Load<TerrainModifiers>("Terrain/Water");
                    tilemaps[layer].gameObject.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
                    tilemaps[layer].gameObject.GetComponent<Rigidbody2D>().sharedMaterial = this.tileMaterial;
                    tilemaps[layer].gameObject.layer = LayerMask.NameToLayer("Trigger");
                    tilemaps[layer].gameObject.GetComponent<TilemapRenderer>().sortingOrder = layer;
                }
                else if (json["layers"][layer]["name"].ToString().Trim('"') == "PLATFORM")
                {
                    string tint = json["layers"][layer]["tintcolor"] != null ? json["layers"][layer]["tintcolor"].ToString() : "#ffffff";
                    float alpha = json["layers"][layer]["opacity"] != null ? json["layers"][layer]["opacity"].AsFloat : 1f;

                    // Render-only tilemap — NO TilemapCollider here. We generate thin,
                    // top-of-surface one-way collider strips after the tiles are laid
                    // (see GeneratePlatformStrips). A full-tile composite collider with
                    // a PlatformEffector2D is unreliable: the player gets embedded in
                    // the thick body and catches on side/bottom edges, especially with
                    // stacked rows. Thin per-run strips fix all of that.
                    tilemaps.Add(layer, CreateTilemap(json["layers"][layer]["name"].ToString().Trim('"'), 0.5f, 0.5f, tint, alpha));
                    tilemaps[layer].gameObject.GetComponent<TilemapRenderer>().sortingOrder = layer;
                    platformLayerID = layer;   // remember so we can build strips later
                }
                else if (json["layers"][layer]["type"].ToString().Trim('"') != "objectgroup")
                {
                    string tint = json["layers"][layer]["tintcolor"] != null ? json["layers"][layer]["tintcolor"].ToString() : "#ffffff";
                    float alpha = json["layers"][layer]["opacity"] != null ? json["layers"][layer]["opacity"].AsFloat : 1f;

                    tilemaps.Add(layer, CreateTilemap(json["layers"][layer]["name"].ToString().Trim('"'), 0.5f, 0.5f, tint, alpha));
                    tilemaps[layer].gameObject.GetComponent<TilemapRenderer>().sortingOrder = layer;
                }

                if (json["layers"][layer]["type"].ToString().Trim('"') == "tilelayer")
                {
                    width = json["layers"][layer]["width"].AsInt;
                    height = json["layers"][layer]["height"].AsInt;
                    MaxWidth = (MaxWidth < width) ? width : MaxWidth;
                    MaxHeight = (MaxHeight < height) ? height : MaxHeight;
                    map.Node = new MapNode[map.LayerCount, width, height];

                    for (int i = 0; i < width * height; i++)
                    {
                        tileID = (json["layers"][layer]["data"][i].AsUInt);
                        xscale = ((tileID & flippedHorizontally) != 0) ? -1f : 1f;
                        yscale = ((tileID & flippedVertically) != 0) ? -1f : 1f;
                        rotation = ((tileID & flippedDiagonally) != 0) ? -90f : 0f;
                        tileID &= ~(flippedHorizontally | flippedVertically | flippedDiagonally);

                        Matrix4x4 matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, Mathf.Round(rotation * 2f), rotation * xscale * yscale), new Vector3(xscale, yscale, 1f));

                        if (tileID != 0)
                        {
                            y = i / width;
                            x = i % width;
                            map.Node[layer, x, y].X = x;
                            map.Node[layer, x, y].Y = y;
                            // Instead of placing the tile immediately, record it so
                            // the chunk manager can place/clear it on demand. This is
                            // what keeps memory bounded on huge maps — only chunks near
                            // the player ever build mesh geometry.
                            if (!_layerCells.ContainsKey(layer))
                                _layerCells[layer] = new Dictionary<Vector3Int, CellTile>();

                            _layerCells[layer][new Vector3Int(x, -y, 0)] = new CellTile
                            {
                                tileId   = (int)tileID,
                                matrix   = matrix,
                                animated = tileAnimations.ContainsKey((int)tileID)
                            };
                            tileCount++;
                        }
                    }
                    if (json["layers"][layer]["parallaxx"] != null || json["layers"][layer]["parallaxy"] != null)
                    {
                        float xOffset = json["layers"][layer]["offsetx"] != null ? json["layers"][layer]["offsetx"].AsFloat : 0f;
                        float yOffset = json["layers"][layer]["offsety"] != null ? json["layers"][layer]["offsety"].AsFloat : 0f;
                        float pxFactor = json["layers"][layer]["parallaxx"] != null ? json["layers"][layer]["parallaxx"].AsFloat : 1f;//json["layers"][layer]["parallaxx"].AsFloat;
                        float pyFactor = json["layers"][layer]["parallaxy"] != null ? json["layers"][layer]["parallaxy"].AsFloat : 1f;//json["layers"][layer]["parallaxy"].AsFloat;
                        AddParallaxEffectToLayer(tilemaps[layer], xOffset, yOffset, pxFactor, pyFactor);
                    }
                }
                else if (json["layers"][layer]["type"].ToString().Trim('"') == "objectgroup")
                {
                    GameObject currentLayer = new GameObject(json["layers"][layer]["name"].ToString().Trim('"'));
                    GameObject currentObject;
                    objectCount += json["layers"][layer]["objects"].Count;

                    currentLayer.transform.SetParent(this.gameObject.transform);
                    for (int i = 0; i < objectCount; i++)
                    {
                        objectID = json["layers"][layer]["objects"][i]["gid"].AsUInt;
                        xscale = ((objectID & flippedHorizontally) != 0) ? -1f : 1f;
                        yscale = ((objectID & flippedVertically) != 0) ? -1f : 1f;
                        rotation = json["layers"][layer]["objects"][i]["rotation"].AsFloat * -1;
                        objectID &= ~(flippedHorizontally | flippedVertically | flippedDiagonally);
                        int propertyCount = 0;

                        if (objectID != 0)
                        {
                            try
                            {
                                string tint = json["layers"][layer]["tintcolor"] != null ? json["layers"][layer]["tintcolor"].ToString() : "#ffffff";
                                float alpha = json["layers"][layer]["opacity"] != null ? json["layers"][layer]["opacity"].AsFloat : 1f;
                                Color color;
                                ColorUtility.TryParseHtmlString(tint.Trim('"'), out color);
                                color = new Color(color.r, color.g, color.b, alpha);
                                width = json["layers"][layer]["objects"][i]["width"].AsInt; //Gets the width of the Object instance...
                                height = json["layers"][layer]["objects"][i]["height"].AsInt; //Gets the height of the Object instance...
                                x = json["layers"][layer]["objects"][i]["x"].AsInt;// + (width / 2);
                                y = json["layers"][layer]["objects"][i]["y"].AsInt * -1 + 16;// + (height / 2);
                                currentObject = Instantiate(objects[(int)objectID], new Vector2(x, y), Quaternion.identity) as GameObject;
                                for(int j = 0; j < json["layers"][layer]["objects"][i]["properties"][j].Count; j++) 
                                {
                                    try
                                    {
                                        switch (json["layers"][layer]["objects"][i]["properties"][j]["name"])
                                        {
                                            case "Destination":
                                                //Debug.Log(json["layers"][layer]["objects"][i]["properties"][j]["value"]);
                                                currentObject.GetComponent<OverworldPortal>().Destination = json["layers"][layer]["objects"][i]["properties"][j]["value"].ToString();
                                                currentObject.GetComponent<Portal>().Destination = json["layers"][layer]["objects"][i]["properties"][j]["value"].ToString();
                                                break;
                                            case "PortalID":
                                                //Debug.Log(json["layers"][layer]["objects"][i]["properties"][j]["value"]);
                                                currentObject.GetComponent<OverworldPortal>().PortalID = json["layers"][layer]["objects"][i]["properties"][j]["value"].AsInt;
                                                currentObject.GetComponent<Portal>().PortalID = json["layers"][layer]["objects"][i]["properties"][j]["value"].AsInt;
                                                break;
                                            case "Direction":
                                                //Debug.Log(json["layers"][layer]["objects"][i]["properties"][j]["value"]);
                                                currentObject.GetComponent<OverworldPortal>().Direction = json["layers"][layer]["objects"][i]["properties"][j]["value"].ToString();
                                                currentObject.GetComponent<Portal>().Direction = json["layers"][layer]["objects"][i]["properties"][j]["value"].ToString();
                                                break;
                                        }
                                    }
                                    catch
                                    {
                                        
                                    }
                                    //Debug.Log(json["layers"][layer]["objects"][i]["properties"][j]["name"]);
                                }
                                //Debug.Log(width);
                                //Debug.Log(height);
                                //Debug.Log(objects[(int)objectID].GetComponent<SpriteRenderer>().sprite.texture.width);
                                //Debug.Log(objects[(int)objectID].GetComponent<SpriteRenderer>().sprite.texture.height);
                                //Debug.Log((float)width / (float)objects[(int)objectID].GetComponent<SpriteRenderer>().sprite.texture.width);
                                xscale *= (float)width / (float)objects[(int)objectID].GetComponent<SpriteRenderer>().sprite.texture.width;
                                yscale *= (float)height / (float)objects[(int)objectID].GetComponent<SpriteRenderer>().sprite.texture.height;
                                currentObject.transform.localScale = new Vector3(xscale, yscale, 1f);
                                currentObject.gameObject.GetComponent<SpriteRenderer>().sortingOrder = layer;
                                currentObject.gameObject.GetComponent<SpriteRenderer>().color = color;
                                currentObject.transform.SetParent(currentLayer.gameObject.transform);
                                currentObject.transform.Rotate(0, 0, rotation, Space.Self);
                            }
                            catch
                            {//(Exception e) {
                                if (OnMessage != null)
                                {
                                    //OnMessage (e.Message.ToString ());
                                    OnMessage("Prefab does not exist in corresponding folder.");
                                }
                            }
                        }
                    }
                }
            }

            // Platform strips are built from the recorded cell data (the tilemap is
            // still empty at this point) so they exist across the whole map.
            if (platformLayerID >= 0)
                GeneratePlatformStrips(platformLayerID);

            // Everything is parsed and recorded — hand off to the chunk manager,
            // which will fill in tiles around the player from here on.
            InitChunking();

            if (OnMessage != null)
            {
                OnMessage.Invoke("TileCount=" + tileCount.ToString());
                OnMessage.Invoke("ObjectCount=" + objectCount.ToString());
            }
        }
        map.TileCount = tileCount;
        map.ObjectCount = objectCount;
        //Debug.Log(MaxWidth);
    }

    private void SetCameraBounds()
    {
        //Bounds bounds = new Bounds(new Vector3(MaxWidth / 2, MaxHeight / 2, 0f), -MaxWidth );
    }

    // =========================================================================
    // Platform collider generation
    // =========================================================================

    /// <summary>
    /// Scans the PLATFORM tilemap for horizontal runs of tiles and creates one
    /// thin BoxCollider2D strip at the TOP of each run, each with a OneWayPlatform
    /// component. Thin strips avoid the embedding/edge-catching problems of a
    /// full-tile composite collider, and per-run strips make stacked platform
    /// rows behave independently.
    /// </summary>
    private void GeneratePlatformStrips(int layer)
    {
        if (!tilemaps.ContainsKey(layer)) return;
        if (!_layerCells.ContainsKey(layer)) return;
        Tilemap tm = tilemaps[layer];
        var cells = _layerCells[layer];

        // Container to keep the generated strips tidy in the hierarchy.
        GameObject container = new GameObject("PLATFORM_Colliders");
        container.transform.SetParent(tm.transform, false);

        const float tileSize  = 16f;   // 1 PPU, 16px tiles
        const float stripThick = 3f;   // thin top strip height in px

        // Work from the recorded cell positions (the tilemap itself is empty until
        // chunks load). Group contiguous horizontal runs per row.
        if (cells.Count == 0) return;

        int xMin = int.MaxValue, xMax = int.MinValue, yMin = int.MaxValue, yMax = int.MinValue;
        foreach (var pos in cells.Keys)
        {
            if (pos.x < xMin) xMin = pos.x;
            if (pos.x > xMax) xMax = pos.x;
            if (pos.y < yMin) yMin = pos.y;
            if (pos.y > yMax) yMax = pos.y;
        }

        for (int cy = yMin; cy <= yMax; cy++)
        {
            int runStartX = int.MinValue;
            int prevX     = int.MinValue;

            for (int cx = xMin; cx <= xMax + 1; cx++)
            {
                bool hasTile = (cx <= xMax) && cells.ContainsKey(new Vector3Int(cx, cy, 0));

                if (hasTile && runStartX == int.MinValue)
                {
                    runStartX = cx;   // start a new run
                    prevX     = cx;
                }
                else if (hasTile && cx == prevX + 1)
                {
                    prevX = cx;        // extend the current run
                }
                else
                {
                    if (runStartX != int.MinValue)
                    {
                        CreatePlatformStrip(container.transform, tm, runStartX, prevX, cy, tileSize, stripThick);
                        runStartX = int.MinValue;
                    }
                    if (hasTile) { runStartX = cx; prevX = cx; }
                }
            }
        }
    }

    private void CreatePlatformStrip(Transform parent, Tilemap tm, int x0, int x1, int cy,
                                     float tileSize, float stripThick)
    {
        int runTiles = (x1 - x0) + 1;

        // World position of the run's top-centre. Cell centre + half a tile up
        // gives the top surface; we place a thin strip just below that top edge.
        Vector3 leftCellCentre  = tm.GetCellCenterWorld(new Vector3Int(x0, cy, 0));
        Vector3 rightCellCentre = tm.GetCellCenterWorld(new Vector3Int(x1, cy, 0));
        float centreX = (leftCellCentre.x + rightCellCentre.x) * 0.5f;
        float topY    = leftCellCentre.y + tileSize * 0.5f;

        GameObject strip = new GameObject($"PlatformStrip_{x0}_{cy}");
        strip.transform.SetParent(parent, false);
        strip.transform.position = new Vector3(centreX, topY - stripThick * 0.5f, 0f);
        strip.layer = LayerMask.NameToLayer("Ground"); // ground-check still detects it

        BoxCollider2D box = strip.AddComponent<BoxCollider2D>();
        box.size      = new Vector2(runTiles * tileSize, stripThick);
        // Trigger, not solid: the physics engine must never resolve a one-way
        // collision, or the player gets depenetrated (hover) or embedded.
        // OneWayPlatformRider on the player handles landing and support exactly.
        box.isTrigger = true;

        strip.AddComponent<OneWayPlatform>();
    }

    // =========================================================================
    // Chunk manager — fills/clears tile cells around the player
    // =========================================================================

    private void InitChunking()
    {
        _cam = Camera.main;

        // Prefer the explicitly-assigned transform; fall back to the Player tag.
        _player = playerTransform;
        if (_player == null)
        {
            var p = GameObject.FindWithTag("Player");
            if (p != null) _player = p.transform;
        }

        if (_player == null)
        {
            // Loud, specific failure. Chunked loading needs something to load
            // around; without it the tilemaps are created but stay EMPTY, which
            // looks exactly like broken art or a broken tileset.
            Debug.LogWarning(
                "[MapManager] No player found — chunked tile loading is disabled and " +
                "NO TILES WILL RENDER in this scene.\n" +
                "Fix: assign 'Player Transform' on the MapManager, or tag your player " +
                "GameObject as 'Player'.", this);
            return;
        }

        _chunkingReady = true;
        _chunkTimer    = 0f;

        // Do an immediate first pass so the area around the player is populated
        // before the first frame is shown.
        UpdateChunks(force: true);
    }

    private void Update()
    {
        if (!_chunkingReady) return;

        _chunkTimer -= Time.deltaTime;
        if (_chunkTimer > 0f) return;
        _chunkTimer = chunkUpdateInterval;

        UpdateChunks(force: false);
    }

    /// <summary>
    /// Determines which chunks should be live based on the camera's visible area
    /// (so it adapts to any resolution), then loads newly-needed chunks and
    /// unloads chunks that are now out of range.
    /// </summary>
    private void UpdateChunks(bool force)
    {
        if (_player == null)
        {
            _player = playerTransform;
            if (_player == null)
            {
                var p = GameObject.FindWithTag("Player");
                if (p != null) _player = p.transform;
            }
            if (_player == null) return;   // already warned in InitChunking
        }
        if (_cam == null) _cam = Camera.main;

        // Figure out the visible half-extent in tiles from the camera. At higher
        // resolutions / zoom the camera shows more, so more chunks load — this is
        // where 1440p vs 1080p naturally scales itself.
        float halfHeightWorld = (_cam != null) ? _cam.orthographicSize : 120f;
        float halfWidthWorld  = (_cam != null) ? halfHeightWorld * _cam.aspect : 200f;

        // World units per tile cell. Cells are 1 unit in the grid by default; the
        // sprites are 16px at 1 PPU, but tilemap cells index by 1, so chunk math is
        // in cell units. We convert the camera's world half-extents (in px) to cells.
        const float pxPerCell = 16f;
        int halfCellsX = Mathf.CeilToInt(halfWidthWorld  / pxPerCell);
        int halfCellsY = Mathf.CeilToInt(halfHeightWorld / pxPerCell);

        // Player position in cell space. Tiles were placed at (x, -y), so the cell
        // Y is the negative of the world tile row.
        Vector3 pp = _player.position;
        int playerCellX = Mathf.RoundToInt(pp.x / pxPerCell);
        int playerCellY = Mathf.RoundToInt(pp.y / pxPerCell);

        int chunkRadiusX = Mathf.CeilToInt((float)halfCellsX / chunkSize) + chunkMargin;
        int chunkRadiusY = Mathf.CeilToInt((float)halfCellsY / chunkSize) + chunkMargin;

        int playerChunkX = Mathf.FloorToInt((float)playerCellX / chunkSize);
        int playerChunkY = Mathf.FloorToInt((float)playerCellY / chunkSize);

        // Build the set of chunks that should be live this update.
        var needed = new HashSet<Vector2Int>();
        for (int cx = playerChunkX - chunkRadiusX; cx <= playerChunkX + chunkRadiusX; cx++)
            for (int cy = playerChunkY - chunkRadiusY; cy <= playerChunkY + chunkRadiusY; cy++)
                needed.Add(new Vector2Int(cx, cy));

        // Load chunks that are needed but not yet loaded.
        foreach (var chunk in needed)
            if (force || !_loadedChunks.Contains(chunk))
                LoadChunk(chunk);

        // Unload chunks that are loaded but no longer needed.
        var toUnload = new List<Vector2Int>();
        foreach (var chunk in _loadedChunks)
            if (!needed.Contains(chunk))
                toUnload.Add(chunk);
        foreach (var chunk in toUnload)
            UnloadChunk(chunk);

        _loadedChunks.Clear();
        foreach (var chunk in needed) _loadedChunks.Add(chunk);
    }

    /// <summary>Places every recorded tile within the chunk's cell range.</summary>
    private void LoadChunk(Vector2Int chunk)
    {
        int x0 = chunk.x * chunkSize;
        int y0 = chunk.y * chunkSize;

        foreach (var layerPair in _layerCells)
        {
            int layer = layerPair.Key;
            if (!tilemaps.ContainsKey(layer)) continue;
            Tilemap tm = tilemaps[layer];
            var cells = layerPair.Value;

            for (int dx = 0; dx < chunkSize; dx++)
            {
                for (int dy = 0; dy < chunkSize; dy++)
                {
                    // Recorded cells use (x, -y); chunk Y maps to negative tile rows.
                    var pos = new Vector3Int(x0 + dx, y0 + dy, 0);
                    if (!cells.TryGetValue(pos, out CellTile ct)) continue;

                    if (ct.animated)
                        tm.SetTile(pos, tileAnimations[ct.tileId]);
                    else
                        tm.SetTile(pos, tiles[ct.tileId]);
                    tm.SetTransformMatrix(pos, ct.matrix);
                }
            }
        }
    }

    /// <summary>Clears the chunk's cells so its mesh memory is freed.</summary>
    private void UnloadChunk(Vector2Int chunk)
    {
        int x0 = chunk.x * chunkSize;
        int y0 = chunk.y * chunkSize;

        foreach (var layerPair in _layerCells)
        {
            int layer = layerPair.Key;
            if (!tilemaps.ContainsKey(layer)) continue;
            Tilemap tm = tilemaps[layer];
            var cells = layerPair.Value;

            for (int dx = 0; dx < chunkSize; dx++)
            {
                for (int dy = 0; dy < chunkSize; dy++)
                {
                    var pos = new Vector3Int(x0 + dx, y0 + dy, 0);
                    if (cells.ContainsKey(pos))
                        tm.SetTile(pos, null);
                }
            }
        }
    }

    private Tilemap CreateTilemap(string tilemapName, float x, float y, string c, float alpha)
    {
        GameObject go = new GameObject(tilemapName);
        Tilemap tm = go.AddComponent<Tilemap>();
        TilemapRenderer tr = go.AddComponent<TilemapRenderer>();

        Color color;
        ColorUtility.TryParseHtmlString(c.Trim('"'), out color);
        tm.tileAnchor = new Vector3(x, y, 0);
        tm.color = new Color(color.r, color.g, color.b, alpha);
        go.transform.SetParent(this.gameObject.transform);
        //tr.sortingLayerName = tilemapName;

        return tm;
    }

    private void AddParallaxEffectToLayer(Tilemap tm, float xOffset, float yOffset, float pxFactor, float pyFactor)
    {
        tm.gameObject.AddComponent<ParallaxLayer>();
        ParallaxLayer parallax = tm.GetComponent<ParallaxLayer>();
        parallax.layerXOffset = xOffset;
        parallax.layerYOffset = yOffset;
        parallax.parallaxXFactor = pxFactor;
        parallax.parallaxYFactor = pyFactor;
        parallax.cam = Camera.main.gameObject;
    }

    public void RemoveItemFromMap(int objectID)
    {
        for (int layer = 0; layer < json["layers"].AsArray.Count; layer++)
        {
            if (json["layers"][layer]["type"].ToString().Trim('"') == "objectgroup")
            {
                if (json["layers"][layer]["objects"][objectID]["id"].AsInt == objectID + 1)
                {
                    json["layers"][layer]["objects"].Remove(objectID);
                }
            }
        }
        //map.MapData = json.ToString();
    }

    private void SaveMap()
    {
        string mapJSON = JsonUtility.ToJson(map.MapData, true);
        BinaryFormatter bf = new BinaryFormatter();
        FileStream file;
        string path = Application.persistentDataPath + "/Temp/Maps/" + mapFile.name + ".dat";

        if (File.Exists(path) != false)
        {
            file = File.Open(path, FileMode.Open);
            bf.Serialize(file, map.MapData);
        }
        else
        {
            Directory.CreateDirectory(Application.persistentDataPath + "/Temp/Maps");
            file = File.Create(Application.persistentDataPath + "/Temp/Maps/" + mapFile.name + ".dat");
            bf.Serialize(file, map.MapData);
        }
    }

    private void LoadMap()
    {
        BinaryFormatter bf = new BinaryFormatter();
        FileStream file;
        string path = Application.persistentDataPath + "/Temp/Maps/" + mapFile.name + ".dat";

        if (File.Exists(path) != false)
        {
            file = File.Open(path, FileMode.Open);
            string data = (string)bf.Deserialize(file);
            file.Close();
            map.MapData = data;
        }
    }
    #endregion
}