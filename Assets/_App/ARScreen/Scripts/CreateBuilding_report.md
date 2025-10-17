# CreateBuilding.cs — Erklär-Report (DE)

Dieses Dokument erklärt, was `CreateBuilding.cs` macht, wie die Geometrie-Pipeline funktioniert, die wichtigsten mathematischen Ideen dahinter und wie du das Skript erweitern oder Fehler suchen kannst. Ziel: schneller Einstieg, verständlich, aber ohne Tiefe zu verlieren.


## Kurzüberblick

- Zweck: Wandelt Schweizer LV95-Polygone (als String) in ein extrudiertes Gebäude-Mesh in Unity um (Deckfläche + optionale Seitenwände).
- Input: String mit LV95-Paaren z. B. `"E,N E,N E,N ..."` (Komma zwischen Ost/Nord, Leerzeichen zwischen Punkten)
- Output: `GameObject` mit `MeshFilter`, `MeshRenderer` und `MeshCollider` plus `BuildingInstance` mit Geo-Infos (WGS84 Lat/Lon).
- Schritte: Parsen → Zentrieren → Triangulieren → Extrudieren → Mesh bauen → GameObject erzeugen.


## Öffentliche Oberfläche und Felder

- Serialisierte Felder
  - `buildingMaterial: Material` — Material für jedes erzeugte Gebäude.
  - `height: float` — Dicke/Höhe der Extrusion (Meter). Es wird der Betrag verwendet; Seiten werden nur erzeugt, wenn die Höhe merklich > 0 ist.

- Haupteinstieg
  - `CreateBuildingFromCoordinates(string coordinates, string name = "Manual", float? altitudeOverride = null, bool? clearExistingOverride = null)`
    - Liest ein geschlossenes LV95-Polygon aus dem String.
    - Berechnet das Polygon-Zentrum (LV95) und wandelt es nach WGS84 (Lat/Lon) um (`ProjNetTransformCH.LV95ToWGS84`).
    - Baut ein lokales 2D-Polygon um das Zentrum (im XZ-Plane).
    - Trianguliert das Polygon (Ear-Clipping); Fallback ist ein einfacher Triangle-Fan.
    - Extrudiert zu einem 3D-Mesh (Deckfläche und ggf. Seiten).
    - Erzeugt ein `GameObject` mit Mesh-Komponenten und gibt `BuildingInstance` zurück.

- Rückgabe-Wrapper
  - `BuildingInstance` enthält:
    - `GameObject` — das erzeugte Objekt (Kind des GameObjects mit `CreateBuilding`)
    - `Latitude`, `Longitude` — Zentrum in WGS84
    - `AltitudeMeters` — übernommener Wert aus `altitudeOverride` (Positionierung in der Welt passiert hier aber nicht automatisch)


## Kurzes Nutzungsbeispiel

```csharp
// Angenommen, die Komponente hängt an einem GameObject
void Start() {
    var creator = GetComponent<CreateBuilding>();

  // LV95-Paare (Ost,Nord). Entweder Schleife explizit schließen oder den letzten Punkt weglassen; beides wird unterstützt.
    string coords = "2680000,1250000 2680010,1250000 2680010,1250010 2680000,1250010 2680000,1250000";

  // Optional: Name und Höhe
    var b = creator.CreateBuildingFromCoordinates(coords, name: "TestSquare", altitudeOverride: 0f);

  // Die Positionierung im Weltraum machst du selbst (dieses Skript baut nur lokale Geometrie).
    if (b != null)
    {
        b.GameObject.transform.position = new Vector3(0, 0, 0);
    // Optional: Drehung/Skalierung an deine AR-Referenz anpassen.
    }
}
```


## Datenfluss (High-Level)

```
"E,N E,N E,N ..."  -->  TryParseLv95Loop  -->  List<Lv95Point> (≥3) + Vorzeichen/Orientierung
                                     |                
                                     v
                           ComputeCentroid (LV95)
                                     |
                                     v
                       LV95ToWGS84 (Lat, Lon) für Metadaten
                                     |
                                     v
                   Localize polygon around centroid (Vector2 list)
                                     |
                                     v
                          TriangulatePolygon (ear clipping)
                             |           \
                             |            -> TriangleFan fallback
                             v
                    BuildThickMesh (Deckfläche [+ Seiten wenn Höhe])
                                     |
                                     v
                Erzeuge GameObject + MeshFilter/Renderer/Collider
                                     |
                                     v
                     return BuildingInstance (GO + Lat/Lon)
```


## Koordinatenräume

- Input: LV95 (Schweizer Projektion) in Metern (Ost/Nord).
- Internes Mesh: Lokaler Raum um das Zentrum. Das Polygon liegt in der XZ-Ebene: X = Ost-Offset, Z = Nord-Offset, Y zeigt nach oben.
- Output: Unity-`GameObject` (lokal unterhalb des `CreateBuilding`-Objekts). Den Weltraum-Transform bestimmst du.
- Geo-Metadaten: WGS84 Lat/Lon aus dem Zentrum (z. B. für AR-Anker oder UI).


## Wichtige Mathematik und Algorithmen (einfach und ausführlich)

Hier die Kernideen mit etwas mehr Detail und weiterhin in verständlicher Sprache.

### 1) Vorzeichen der Polygonfläche (Orientierung)

Warum wichtig? Die Orientierung (CCW vs. CW) beeinflusst, wie Dreiecke gewunden werden und in welche Richtung Normalen zeigen. Falsche Orientierung → falsche Normalen/Seiten.

Formel (für Punkte `$p_i=(x_i,y_i)$`):

$$
A = \frac{1}{2} \sum_{i=0}^{n-1} (x_i y_{i+1} - x_{i+1} y_i)
$$

Lesart:
- `A > 0` → Punkte laufen gegen den Uhrzeigersinn (CCW)
- `A < 0` → im Uhrzeigersinn (CW)

Im Code: `SignedArea(Vector2)` und `ComputeSignedArea(Lv95Point)` (gleiche Logik). Das Vorzeichen wird später genutzt, um Seiten-Normalen ggf. zu flippen.

### 2) Schwerpunkt (Centroid)

Warum wichtig? Wir zentrieren das Polygon um den Schwerpunkt, damit Geometriedaten klein bleiben (numerisch stabiler, leichter zu debuggen). Den Schwerpunkt konvertieren wir zudem nach WGS84, um Lat/Lon zu speichern.

Formeln (für nicht-degenerierte Polygone):

$$
\bar{x} = \frac{1}{6A} \sum_{i=0}^{n-1} (x_i + x_{i+1})(x_i y_{i+1} - x_{i+1} y_i)
$$
$$
\bar{y} = \frac{1}{6A} \sum_{i=0}^{n-1} (y_i + y_{i+1})(x_i y_{i+1} - x_{i+1} y_i)
$$

Sonderfall: Ist `|A|` sehr klein (quasi Linie/Punkt), nehmen wir einfach den Mittelwert der Punkte.

### 3) Triangulation (Ear-Clipping) — genauer erklärt

Ziel: Das (löcherlose) Polygon in Dreiecke zerlegen. „Ear-Clipping“ ist ein bewährtes, gut verständliches Verfahren dafür.

Begriffe:
- Konvexer Eckpunkt: An der Ecke „biegt“ das Polygon nach außen (Innenwinkel < 180°). Das testen wir über Kreuzprodukt/Vorzeichen.
- Ohr: Das Dreieck `(prev, cur, next)` an einer konvexen Ecke, in dessen Innerem KEIN weiterer Polygonpunkt liegt.

Ablauf Schritt für Schritt:
1. Wir stellen sicher, dass die Indizes CCW sortiert sind (über das Flächenvorzeichen).
2. Für jeden Eckpunkt prüfen wir:
  - Konvexitätstest: `(b - a) x (c - b)` soll positiv sein (bei CCW-Orientierung). Sonst ist es eine Reflex-Ecke → kein Ohr.
  - Punkt-im-Dreieck-Test: Kein anderer verfügbarer Polygonpunkt darf im Dreieck `(a,b,c)` liegen. Dafür wird baryzentrische Prüfung genutzt (`PointInTriangle`).
3. Finden wir ein Ohr, fügen wir das Dreieck zu den Ergebnissen hinzu und entfernen den mittleren Index `cur` aus der Liste.
4. Weiter, bis nur noch zwei Kanten übrig sind (dann sind alle Dreiecke gefunden) oder bis eine Sicherheitsgrenze erreicht ist.

Komplexität: Typisch `O(n^2)` für n Punkte (für Gebäudegrundrisse völlig ausreichend).

Typische Fehlerfälle und Abhilfe:
- Numerische Degeneration (fast kollineare Punkte): Ohren werden schwer zu erkennen → der Code hat eine Guard/Abbruchbedingung.
- Stark eingedellte (nicht-konvexe) Polygone: Ear-Suche kann stocken → es gibt einen Fallback (Triangle-Fan), der bei konvexen Formen gut funktioniert.
- Doppelte/nahezu gleiche Punkte: Können zu sehr kurzen Kanten führen → Seitenaufbau überspringt solche Kanten; Parser entfernt doppelte Endpunkte.

Im Code relevant:
- `TriangulatePolygon` steuert den Ohr-Such-Loop mit einer Schutzvariable (`guard`).
- `IsEar` prüft Konvexität und „kein Punkt im Ohr-Dreieck“.
- `PointInTriangle` nutzt baryzentrische Koordinaten und eine Toleranz gegen numerisches Rauschen.
- Fallback: `TriangleFan` erzeugt Dreiecke `(0, i, i+1)` für i=1..n-2.

### 4) Extrusion & Normalen

- Deckfläche: entsteht direkt aus den Triangles der Triangulation, auf `y = +H/2`. Normalen = `Vector3.up`.
- Seiten: Für jede Polygonkante bauen wir ein Rechteck aus vier Punkten (oben/unten) und zerlegen es in zwei Dreiecke. Die Seitennormale ist senkrecht zur Kante und zeigt nach außen. Falls die Gesamt-Orientierung negativ ist, wird die Normale geflippt.
- Boden: nicht vorhanden. Für einen geschlossenen Körper kann man die Deckfläche nach unten spiegeln und Indizes/Winding anpassen.


## Wichtige Sonderfälle, die abgedeckt sind

- Zu wenige Punkte (< 3) → Abbruch.
- Letzter Punkt = erster Punkt → Duplikat wird entfernt.
- Kanten mit nahezu 0-Länge (Seiten) → werden übersprungen.
- Fläche nahezu 0 → Schwerpunkt = Mittelwert aller Punkte.
- Triangulation scheitert → Triangle-Fan-Fallback; wenn auch das nicht reicht, wird nichts gespawnt.
- Negative `height` → Betrag wird genutzt (Richtung egal).
- Sehr kleine `height` → Seiten werden weggelassen (vermeidet Artefakte).


## Materialien, UVs und Darstellung

- Material: `buildingMaterial` (falls gesetzt) wird auf den `MeshRenderer` gelegt.
- UVs:
  - Deckfläche nutzt aktuell die lokalen `(x, z)`-Koordinaten (Meter) direkt als UVs. Für große Gebäude ist eine Skalierung sinnvoll.
  - Seitenflächen haben einfache `[0..1]`-Streifen pro Kante.
- Normalen: Oben nach oben; an den Seiten pro Kante nach außen. Bounds werden am Ende neu berechnet.

Verbesserungsvorschlag (einfach, wenig Risiko): UV-Skalierung als Feld z. B. `Vector2 uvScale = new(0.1f, 0.1f)` und Top-UVs als `(p.x * uvScale.x, p.y * uvScale.y)` mappen.


## Vertrag und Zuständigkeiten

- Inputs
  - `coordinates`: String mit LV95-Punkten in Metern, Form `"east,north"`, getrennt durch Leerzeichen.
  - Optional `name`, `altitudeOverride`.
- Outputs
  - `BuildingInstance` bei Erfolg, sonst `null`.
  - Erzeugtes Kind-`GameObject` namens `ProjectedBuilding_<name>` (oder generisch).
- Deine Aufgaben
  - Welt-Transform setzen (Position/Rotation/Skalierung).
  - Höhe/AR-Anker im Gesamtsystem berücksichtigen (dieses Skript speichert `AltitudeMeters`, setzt die Y-Position aber nicht automatisch).


## Wo die Geokonvertierung passiert

- `ProjNetTransformCH.LV95ToWGS84(east, north, out lat, out lon)`
  - Wandelt LV95 (Meter) in WGS84 (Lat/Lon) um.
  - Ergebnis wird in `BuildingInstance` gespeichert (z. B. für AR-Anker/Labels). Das Unity-Transform wird hier nicht daraus gesetzt.


## Erweiterungsideen (sicher umsetzbar)

- Boden schließen (Deckfläche bei `-h/2` spiegeln, Winding flippen, Dreiecke hinzufügen).
- UV-Skalierung für Deckfläche (saubere Wiederholung großer Texturen).
- Variable Höhe pro Gebäude (Parameter statt nur serialisiertes Feld).
- Innenringe/„Holes“ unterstützen (aktuelles Ear-Clipping geht von einfachem Polygon ohne Löcher aus).
- Optionale Vertexfarben oder zweite UVs (Lightmapping).


## Troubleshooting-Checkliste

- Gebäude erscheint nicht?
  - Parsed der String zu ≥ 3 Punkten?
  - Kollabiert das Polygon nach dem Zentrieren (identische Punkte, NaN/Inf)?
  - Ist `buildingMaterial` gesetzt (oder nutze das Standardmaterial)?
  - Konsole prüfen: das Skript loggt Warnungen im Fehlerfall.
- Mesh wirkt „innen außen“?
  - Orientierung/Winding wird korrigiert, aber eigene Änderungen können das brechen. Normalen im Scene-View checken.
- Seiten fehlen?
  - `height` zu klein. Erhöhe `height`.


## Anhang A — Methoden-Map (Wer ruft wen?)

- `CreateBuildingFromCoordinates`
  - `TryParseLv95Loop` → `ComputeSignedArea`
  - `ComputeCentroid`
  - `ProjNetTransformCH.LV95ToWGS84`
  - `BuildLocalPolygon`
  - `TriangulatePolygon` → `IsEar` → `PointInTriangle`, `SignedArea`
  - `TriangleFan` (Fallback)
  - `BuildThickMesh` → `SignedArea`
  - `SpawnBuilding` (Mesh + Komponenten) → gibt `BuildingInstance` zurück


## Anhang B — Beispiel-Koordinatenstrings

- Einfaches Quadrat (10 m):

```
2680000,1250000 2680010,1250000 2680010,1250010 2680000,1250010 2680000,1250000
```

- Ohne Wiederholung des letzten Punkts (auch gültig):

```
2680000,1250000 2680010,1250000 2680010,1250010 2680000,1250010
```


---

Hinweis für Maintainer: Dieser Report deckt Intention, Mathe und Verhalten von `CreateBuilding.cs` ab, damit du sicher UVs/Materialien anpasst, einen Boden ergänzt oder die Triangulation austauschst.
