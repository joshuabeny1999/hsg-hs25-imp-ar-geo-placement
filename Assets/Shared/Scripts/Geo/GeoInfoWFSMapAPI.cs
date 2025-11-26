using System.Collections;
using Shared.Scripts.Geo;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class GeoInfoWFSMapAPI : MonoBehaviour
{
    [Header("UI Settings")]
    [Tooltip("Assign the Image component from your Canvas here")]
    public Image targetCanvasImage;

    [Header("Debug Settings")]
    [SerializeField, Tooltip("Use manual LV95 coordinates instead of the device GPS (for in-editor testing).")]
    private bool useDebugCoordinates = false;
    [SerializeField] private double debugLv95CoordinatesEast = 2743009.24f;
    [SerializeField] private double debugLv95CoordinatesNorth = 1252728.11f;

    // Base URL from your documentation
    private const string BaseUrl = "https://www.geoportal.ch/ch/map/40";

    /// <summary>
    /// Call this function to load the map.
    /// </summary>
    /// <param name="scope">The scale of the map (e.g., 500, 1000, 5000). Smaller number = more zoomed in.</param>
    public void FetchMap(int scope)
    {
        StartCoroutine(GetMapRequest(scope));
    }

    private IEnumerator GetMapRequest(int scale)
    {
        double lv95East, lv95North;

        if (useDebugCoordinates)
        {
            lv95East = debugLv95CoordinatesEast;
            lv95North = debugLv95CoordinatesNorth;
        }
        else
        {
            var lastKnown = Input.location.lastData;
            ProjNetTransformCH.WGS84ToLV95(lastKnown.latitude, lastKnown.longitude, out lv95East, out lv95North);
        }

        Debug.Log("GeoInfoWFSMapAPI : Requesting Map: " + lv95East + ", " + lv95North + " at scale " + scale);


        string url = $"{BaseUrl}?y={lv95East}&x={lv95North}&scale={scale}&rotation=0&topic=coord&format=image/png";

        Debug.Log("GeoInfoWFSMapAPI : Requesting Map: " + url);

        using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(url))
        {
            yield return uwr.SendWebRequest();

            if (uwr.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Map Error: " + uwr.error);
            }
            else
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(uwr);

                if (targetCanvasImage != null)
                {
                    Sprite mapSprite = Sprite.Create(
                        texture, 
                        new Rect(0, 0, texture.width, texture.height), 
                        new Vector2(0.5f, 0.5f)
                    );

                    targetCanvasImage.sprite = mapSprite;
                    targetCanvasImage.type = Image.Type.Simple;
                    targetCanvasImage.preserveAspect = true;
                    
                    targetCanvasImage.transform.parent.gameObject.SetActive(true);
                }
            }
        }
    }
}
