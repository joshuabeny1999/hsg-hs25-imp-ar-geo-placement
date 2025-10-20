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

  // Die Positionierung im world space machst du selbst (dieses Skript baut nur lokale Geometrie).
    if (b != null)
    {
        b.GameObject.transform.position = new Vector3(0, 0, 0);
  // Optional: rotation/scale an deine AR-Referenz anpassen.
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
- Output: Unity-`GameObject` (lokal unterhalb des `CreateBuilding`-Objekts). Den world-space Transform bestimmst du.
- Geo-Metadaten: WGS84 Lat/Lon aus dem Zentrum (z. B. für AR-Anker oder UI).


## Wichtige Mathematik und Algorithmen (einfach und ausführlich)

Hier die Kernideen mit etwas mehr Detail und weiterhin in verständlicher Sprache.

### 1) Vorzeichen der Polygonfläche (Orientierung)

Warum wichtig? Die Orientierung (CCW (counter-clockwise) vs. CW (clockwise)) beeinflusst, wie Dreiecke gewunden werden und in welche Richtung Normalen zeigen. Falsche Orientierung → falsche Normalen/Seiten.

Formel (für Punkte `$p_i=(x_i,y_i)$`):

$$
A = \frac{1}{2} \sum_{i=0}^{n-1} (x_i y_{i+1} - x_{i+1} y_i)
$$

Notation (damit keine Variable unerklärt bleibt):
- `A` ist die vorzeichenbehaftete Fläche des Polygons in Quadratmetern (m²). `A > 0` bedeutet CCW (counter-clockwise), `A < 0` bedeutet CW (clockwise).
- `n` ist die Anzahl der Polygonpunkte.
- `(x_i, y_i)` sind die 2D-Koordinaten des i-ten Punkts (je nach Kontext LV95 in Metern oder lokalisierte Offsets).
- `i+1` wird modulo `n` verstanden (d. h. nach dem letzten Punkt kommt wieder der erste).

Lesart:
- `A > 0` → Punkte laufen gegen den Uhrzeigersinn (CCW (counter-clockwise))
- `A < 0` → im Uhrzeigersinn (CW (clockwise))

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

Hinweis zur Notation:
- `A` ist die oben definierte vorzeichenbehaftete Fläche.
- `\bar{x}`, `\bar{y}` sind die Koordinaten des Schwerpunkts (Centroid) in denselben Einheiten wie `(x_i, y_i)`.
- Der Index `i+1` ist wieder modulo `n`.

Sonderfall: Ist `|A|` sehr klein (quasi Linie/Punkt), nehmen wir einfach den Mittelwert der Punkte.

### 3) Triangulation (Ear-Clipping) — genauer erklärt

Ziel: Das (löcherlose) Polygon in Dreiecke zerlegen. „Ear-Clipping“ ist ein bewährtes, gut verständliches Verfahren dafür.

Begriffe:
- Konvexer Eckpunkt: An der Ecke „biegt“ das Polygon nach außen (Innenwinkel < 180°). Das testen wir über Kreuzprodukt/Vorzeichen.
- Ohr: Das Dreieck `(prev, cur, next)` an einer konvexen Ecke, in dessen Innerem KEIN weiterer Polygonpunkt liegt.

Ablauf Schritt für Schritt:
1. Wir stellen sicher, dass die Indizes CCW (counter-clockwise) sortiert sind (über das Flächenvorzeichen).
2. Für jeden Eckpunkt prüfen wir:
  - Konvexitätstest: `(b - a) x (c - b)` soll positiv sein (bei CCW (counter-clockwise)-Orientierung). Sonst ist es eine Reflex-Ecke → kein Ohr.
  - Punkt-im-Dreieck-Test: Kein anderer verfügbarer Polygonpunkt darf im Dreieck `(a,b,c)` liegen. Dafür wird baryzentrische Prüfung genutzt (`PointInTriangle`).
3. Finden wir ein Ohr, fügen wir das Dreieck zu den Ergebnissen hinzu und entfernen den mittleren Index `cur` aus der Liste.
4. Weiter, bis nur noch zwei Kanten übrig sind (dann sind alle Dreiecke gefunden) oder bis eine Sicherheitsgrenze erreicht ist.

Komplexität: Typisch `O(n^2)` für n Punkte (für Gebäudegrundrisse völlig ausreichend).

Typische Fehlerfälle und Abhilfe (ausführlich):
- Numerische Degeneration (fast kollineare Punkte):
  - Problem: Konvexe Ecken werden numerisch „flach“ → Kreuzprodukt nähert sich 0, IsEar wird unsicher.
  - Abhilfe: Der Code besitzt eine Guard/Abbruchbedingung (z. B. max. Iterationen). Zusätzlich hilft Vorverarbeitung: sehr nahe Punkte zusammenfassen (Epsilon-Snap), nahezu identische Kanten entfernen.
- Stark eingedellte (nicht-konvexe) Polygone:
  - Problem: Es gibt länger keine gültigen Ohren, weil viele Reflex-Ecken vorhanden sind.
  - Abhilfe: Fallback (Triangle-Fan). Für konvexe Polygone liefert er korrekte Triangles; bei stark nicht-konvexen Formen ist er nur eine Notlösung.
- Doppelte/nahezu gleiche Punkte:
  - Problem: Sehr kurze Kanten → Seitenaufbau erzeugt winzige/doppelte Dreiecke oder degenerierte Quads.
  - Abhilfe: Parser entfernt doppelte Endpunkte; Seitenaufbau überspringt 0-Längen-Kanten.

Triangle-Fan — was ist das, wann wird er genutzt?
- Idee: Ein Fixpunkt (hier Index 0) wird mit jedem aufeinanderfolgenden Kantenpaar verbunden → Dreiecke `(0, i, i+1)` für `i = 1 .. n-2`.
- Geeignet für: Konvexe Polygone oder Polygone, die in Fan-Richtung keine Selbstüberschneidungen erzeugen.
- Vorteile: Sehr einfach, fehlertolerant, keine Ear-Suche notwendig.
- Nachteile: Bei nicht-konvexen Polygoneilen können Dreiecke außerhalb des eigentlichen Polygons liegen (geometrisch falsch) oder Überschneidungen erzeugen.
- Winding/Orientierung: Bleibt bei CCW (counter-clockwise)-Eckpunktreihenfolge erhalten. Ist die Reihenfolge CW (clockwise), müssen Indizes gespiegelt oder vorab die Orientierung korrigiert werden (im Code passiert die Orientierungskorrektur bereits zuvor über das Flächenvorzeichen).
- Einsatz im Code: Nur als Fallback, wenn Ear-Clipping nicht die erwartete Anzahl von Dreiecken liefert (z. B. wegen numerischer Probleme). Besser ein grob korrektes Ergebnis als gar keines, besonders für einfache Grundrisse.

Im Code relevant (mehr Details):
- `TriangulatePolygon`:
  - Steuert den Ohr-Such-Loop mit einer Schutzvariable (`guard`), damit wir nicht unendlich iterieren.
  - Erwartete Dreiecksanzahl ist `(n-2)`. Wird sie nicht erreicht, wird der Fallback aktiviert.
- `IsEar`:
  - Konvexitätstest per Kreuzprodukt. Ein kleiner Epsilon-Spielraum verhindert Fehleinstufungen bei nahezu kollinearen Punkten.
  - Punkt-im-Dreieck-Test stellt sicher, dass kein verfügbarer Punkt innerhalb des Kandidatendreiecks liegt (verhindert Überschneidungen).
- `PointInTriangle`:
  - Baryzentrische Prüfung; wenn der Nenner (Doppelfläche) zu klein ist, gilt das Dreieck als degeneriert.
  - Nutzt Toleranzen (z. B. `1e-6`) gegen numerisches Rauschen.
- Fallback `TriangleFan`:
  - Bildet Dreiecke `(0, i, i+1)` für `i = 1 .. n-2`.
  - Erwartet sinnvolle Eckpunktreihenfolge (idealerweise CCW (counter-clockwise)) für korrekte Winding/Normalen.

### 4) Extrusion & Normalen

Weiterführende Ressourcen:
- Ear Clipping Overview (YouTube): https://www.youtube.com/watch?v=QAdfkylpYwc&pp=ygUMZWFyIGNsaXBwaW5n
- Punkt-im-Dreieck-Tests (Kreuzprodukt, baryzentrisch, anschaulich erklärt): https://claude.ai/public/artifacts/472e7a2e-e102-4489-a7f1-b512d01a7d5a

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

 


## Vertrag und Zuständigkeiten

- Inputs
  - `coordinates`: String mit LV95-Punkten in Metern, Form `"east,north"`, getrennt durch Leerzeichen.
  - Optional `name`, `altitudeOverride`.
- Outputs
  - `BuildingInstance` bei Erfolg, sonst `null`.
  - Erzeugtes Kind-`GameObject` namens `ProjectedBuilding_<name>` (oder generisch).
- Deine Aufgaben
  - world-space Transform setzen (Position/Rotation/Skalierung).
  - Höhe/AR-Anker im Gesamtsystem berücksichtigen (dieses Skript speichert `AltitudeMeters`, setzt die Y-Position aber nicht automatisch).


## Wo die Geokonvertierung passiert

- `ProjNetTransformCH.LV95ToWGS84(east, north, out lat, out lon)`
  - Wandelt LV95 (Meter) in WGS84 (Lat/Lon) um.
  - Ergebnis wird in `BuildingInstance` gespeichert (z. B. für AR-Anker/Labels). Das Unity Transform wird hier nicht daraus gesetzt.


 


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


## Anhang C — Feature-Matrix (Implementierter Stand)

| Feature | Implementiert | Hinweis |
| --- | --- | --- |
| LV95-Parsing (String → Punkte) | Ja | Duplikat des Endpunkts wird entfernt, Format „east,north“ mit Leerzeichen zwischen Punkten |
| Schwerpunktberechnung (LV95) | Ja | Flächenformel; Fallback auf Punktmittelwert bei nahezu 0-Fläche |
| WGS84-Umrechnung (Lat/Lon) | Ja | `ProjNetTransformCH.LV95ToWGS84` auf Schwerpunkt |
| Lokalisierung ins XZ (lokaler Raum) | Ja | Zentrieren um Schwerpunkt, X=Ost-Offset, Z=Nord-Offset |
| Triangulation (Ear-Clipping) | Ja | Mit Guard und „IsEar“/`PointInTriangle`; Orientierung wird beachtet |
| Fallback Triangulation (Triangle-Fan) | Ja | Wenn nicht `(n-2)` Dreiecke erreicht werden |
| Extrusion (Deckfläche) | Ja | Auf `y=+H/2`, Normalen nach oben |
| Seitenflächen | Ja | Nur bei signifikanter `height`; 0-Längen-Kanten werden übersprungen |
| Bodenfläche (Bottom Cap) | Nein | Nicht implementiert |
| Materialzuweisung | Ja | `MeshRenderer` nutzt `buildingMaterial` (falls gesetzt) |
| UVs Deckfläche | Ja | Planar aus lokalen `(x,z)` (Meter) |
| UVs Seiten | Ja | Einfache Streifen pro Kante |
| MeshCollider | Ja | Collider wird am erzeugten GameObject gesetzt |
| Orientierungskorrektur (CCW/CW) | Ja | Über vorzeichenbehaftete Fläche; wirkt sich auf Winding/Normalen aus |
| Unterstützung für Löcher (Holes) | Nein | Nicht implementiert |
| Optionale Vorverarbeitung (Snap/Dedupe) | Nein | Nicht implementiert |
