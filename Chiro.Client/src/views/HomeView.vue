<script setup>
import { ref, onMounted, onUnmounted, nextTick } from "vue";
import { LMap, LTileLayer, LGeoJson, LControl } from "@vue-leaflet/vue-leaflet";
import L from "leaflet";
import proj4 from "proj4";
import { sendMessageToBackend } from "../services/photinoService";

// Register EPSG:2154 (Lambert 93) definition for projection
proj4.defs(
  "EPSG:2154",
  "+proj=lcc +lat_1=49 +lat_2=44 +lat_0=46.5 +lon_0=3 +x_0=700000 +y_0=6600000 +ellps=GRS80 +towgs84=0,0,0,0,0,0,0 +units=m +no_defs"
);

// Helper to recursively reproject GeoJSON coordinates from Lambert93 to displayable WGS84
const projectToWGS84 = (geojson) => {
  if (!geojson) return null;
  const data = JSON.parse(JSON.stringify(geojson)); // Deep clone

  const transformCoords = (coords, type) => {
    if (type === "Point") {
      const wgs = proj4("EPSG:2154", "EPSG:4326", [coords[0], coords[1]]);
      coords[0] = wgs[0];
      coords[1] = wgs[1];
    } else if (type === "LineString" || type === "MultiPoint") {
      for (let i = 0; i < coords.length; i++) {
        const wgs = proj4("EPSG:2154", "EPSG:4326", [coords[i][0], coords[i][1]]);
        coords[i][0] = wgs[0];
        coords[i][1] = wgs[1];
      }
    } else if (type === "Polygon" || type === "MultiLineString") {
      coords.forEach(ring => transformCoords(ring, "LineString"));
    } else if (type === "MultiPolygon") {
      coords.forEach(polygon => transformCoords(polygon, "Polygon"));
    }
  };

  if (data.type === "Feature") {
    transformCoords(data.geometry.coordinates, data.geometry.type);
  } else if (data.type === "FeatureCollection") {
    data.features.forEach(f => transformCoords(f.geometry.coordinates, f.geometry.type));
  } else {
    transformCoords(data.coordinates, data.type);
  }
  return data;
};

// Selected file path (absolute, from native dialog)
const selectedFilePath = ref(null);
const selectedFileName = ref(null);

// Search Radius (Intervals: 5, 10, 15, 20)
const searchRadius = ref(20);

// Data Table Setup (Zones)
const headers = ref([
  { title: "Type de Zone", key: "type", align: "start" },
  { title: "Code ZNIEFF/N2000", key: "code" },
  { title: "Nom", key: "name" },
  { title: "Distance (km)", key: "distance" },
  { title: "Orientation", key: "orientation" },
]);
const results = ref([]);

// Data Table Setup (Species)
const activeTab = ref("zones");
const speciesSearch = ref("");
const speciesResults = ref([]);
const speciesHeaders = ref([
  { title: "Nom Scientifique", key: "scientificName", align: "start" },
  { title: "Nom Vernaculaire", key: "vernacularName" },
  { title: "Groupe", key: "group" },
  { title: "Statuts", key: "statusSummary" },
  { title: "Zones", key: "zonesLabel" },
  { title: "Score Enjeu", key: "endangermentScore", align: "end" },
]);
const loading = ref(false);
const picking = ref(false);
const errorMessage = ref(null);

// Map Setup
const zoom = ref(6);
const center = ref([46.2276, 2.2137]); // Centered on France
let mapInstance = null;

const perimeterGeoJson = ref(null);
const zonesGeoJson = ref([]);

// -------------------------------------------------------------
// Layout Resizing State & Methods
// -------------------------------------------------------------
const sidebarWidth = ref(300);
const mapHeight = ref(40); // 40vh initially
const isDraggingSidebar = ref(false);
const isDraggingMap = ref(false);

const startDragSidebar = () => {
  isDraggingSidebar.value = true;
  document.body.style.cursor = "col-resize";
  document.body.style.userSelect = "none";
};

const startDragMap = () => {
  isDraggingMap.value = true;
  document.body.style.cursor = "row-resize";
  document.body.style.userSelect = "none";
};

const stopDrag = () => {
  // If we just finished dragging, force Leaflet to recalculate its canvas size
  if (isDraggingSidebar.value || isDraggingMap.value) {
    if (mapInstance) {
      setTimeout(() => mapInstance.invalidateSize(), 50);
    }
  }
  isDraggingSidebar.value = false;
  isDraggingMap.value = false;
  document.body.style.cursor = "default";
  document.body.style.userSelect = "auto";
};

const onDrag = (e) => {
  if (isDraggingSidebar.value) {
    // Determine new width based on mouse clientX with basic bounds
    const newWidth = e.clientX;
    if (newWidth > 200 && newWidth < window.innerWidth * 0.6) {
      sidebarWidth.value = newWidth;
    }
  } else if (isDraggingMap.value) {
    // Determine new height in vh based on mouse clientY
    const newHeightVh = (e.clientY / window.innerHeight) * 100;
    if (newHeightVh >= 5 && newHeightVh < 90) {
      mapHeight.value = newHeightVh;
    }
  }
};

onMounted(() => {
  window.addEventListener("mousemove", onDrag);
  window.addEventListener("mouseup", stopDrag);
});

onUnmounted(() => {
  window.removeEventListener("mousemove", onDrag);
  window.removeEventListener("mouseup", stopDrag);
});
// -------------------------------------------------------------

const getRowProps = ({ item }) => {
  if (item.orientation === "Dans le périmètre") {
    // Apply bold text and light amber background for rows inside the perimeter
    return { class: "bg-amber-lighten-4 font-weight-bold" };
  }
  return {};
};

// Open the native OS file dialog via the C# backend
const pickFile = async () => {
  picking.value = true;
  errorMessage.value = null;

  try {
    const response = await sendMessageToBackend("pickFile", {});
    console.log("pickFile response:", response);

    if (response.status === "success" && response.data) {
      selectedFilePath.value = response.data.filePath;
      // Extract just the filename for display
      const parts = response.data.filePath.split("/");
      selectedFileName.value = parts[parts.length - 1];
    } else if (response.status === "cancelled") {
      // User cancelled — do nothing
    } else {
      errorMessage.value = response.message || "Erreur lors de la sélection du fichier.";
    }
  } catch (error) {
    console.error("Error picking file:", error);
    errorMessage.value = "Erreur de communication avec le backend.";
  } finally {
    picking.value = false;
  }
};

// Clear selected file
const clearFile = () => {
  selectedFilePath.value = null;
  selectedFileName.value = null;
  results.value = [];
  speciesResults.value = [];
  perimeterGeoJson.value = null;
  zonesGeoJson.value = [];
};

// Handle Submission — send the absolute file path to the backend
const handleSubmit = async () => {
  if (!selectedFilePath.value) {
    errorMessage.value = "Veuillez sélectionner un fichier de périmètre.";
    return;
  }

  loading.value = true;
  errorMessage.value = null;

  try {
    const payload = {
      filePath: selectedFilePath.value,
      radiusKm: searchRadius.value,
    };

    console.log("Sending processPerimeter to backend:", payload);
    const response = await sendMessageToBackend("processPerimeter", payload, 120000);
    console.log("Received from backend:", response);

    if (response.status === "success" && response.data) {
      if (response.data.perimeter) {
        perimeterGeoJson.value = projectToWGS84(JSON.parse(response.data.perimeter));
      }

      if (response.data.zones) {
        zonesGeoJson.value = response.data.zones.filter(z => z.geoJson).map(z => ({
          geojson: projectToWGS84(JSON.parse(z.geoJson)),
          isInside: z.isInside,
          id: z.id
        }));

        results.value = response.data.zones.map((zone) => ({
          type: zone.type,
          code: zone.code,
          name: zone.name,
          distance: (zone.distanceMeters / 1000).toFixed(2),
          orientation: zone.isInside ? "Dans le périmètre" : "—",
        }));
      }

      if (response.data.species) {
        speciesResults.value = response.data.species.map(s => {
          // Identify the highest Red List status for scoring
          let maxScore = 0;
          
          s.statuses.forEach(st => {
            const code = st.code.toUpperCase();
            if (code.includes("CR")) maxScore = Math.max(maxScore, 5);
            else if (code.includes("EN")) maxScore = Math.max(maxScore, 4);
            else if (code.includes("VU")) maxScore = Math.max(maxScore, 3);
            else if (code.includes("NT")) maxScore = Math.max(maxScore, 2);
            else if (code.includes("LC") || code.includes("LR/LC")) maxScore = Math.max(maxScore, 1);
          });

          if (maxScore === 0 && s.isDeterminant) maxScore = 1.5;

          return {
            scientificName: s.scientificName,
            vernacularName: s.vernacularName || "—",
            group: s.group1Inpn || s.group2Inpn || "Inconnu",
            statusSummary: s.statuses.map(st => st.code).join(", ") || "—",
            zonesLabel: s.zones.map(z => z.zoneName).join(", "),
            endangermentScore: maxScore
          };
        });
      }

      // Snap map camera to the uploaded perimeter
      nextTick(() => {
        if (mapInstance && perimeterGeoJson.value) {
          const bounds = L.geoJSON(perimeterGeoJson.value).getBounds();
          if (bounds.isValid()) {
            mapInstance.fitBounds(bounds, { padding: [20, 20] });
          }
        }
      });
    } else {
      errorMessage.value = response.message || "Erreur inconnue du backend.";
    }
  } catch (error) {
    console.error("Error contacting backend:", error);
    errorMessage.value = "Erreur de communication avec le backend.";
  } finally {
    loading.value = false;
  }
};

// Ensure the Map resizes correctly after Vuetify layout completion
const onMapReady = (mapObject) => {
  mapInstance = mapObject;
  setTimeout(() => {
    mapObject.invalidateSize();
  }, 100);
};
</script>

<template>
  <v-container fluid class="fill-height pa-0 ma-0" style="height: 100vh">
    <div class="d-flex fill-height" style="width: 100%">
      <!-- Left Sidebar: Controls -->
      <div
        class="sidebar-container fill-height d-flex flex-column elevation-4"
        :style="{ width: `${sidebarWidth}px`, minWidth: '200px', flexShrink: 0 }"
      >
        <div class="pa-4 bg-primary text-white flex-grow-0">
          <h2 class="text-h5 font-weight-bold">Chiro Diagnostics</h2>
          <p class="text-subtitle-2 mb-0">Paramètres de recherche</p>
        </div>

        <v-divider></v-divider>

        <div class="pa-4 flex-grow-1 overflow-y-auto">
          <v-form @submit.prevent="handleSubmit">
            <!-- Native File Picker -->
            <div class="mb-6">
              <v-text-field
                :model-value="selectedFileName || ''"
                label="Périmètre d'étude (.shp, .gpkg)"
                prepend-icon="mdi-map-marker-path"
                variant="outlined"
                color="primary"
                hint="Cliquez sur 'Parcourir' pour sélectionner votre fichier"
                persistent-hint
                readonly
                @click="pickFile"
              >
                <template v-slot:append>
                  <v-btn
                    v-if="selectedFilePath"
                    icon="mdi-close"
                    size="small"
                    variant="text"
                    @click.stop="clearFile"
                  ></v-btn>
                </template>
              </v-text-field>

              <v-btn
                color="secondary"
                variant="tonal"
                block
                class="mt-2"
                prepend-icon="mdi-folder-open"
                :loading="picking"
                @click="pickFile"
              >
                Parcourir...
              </v-btn>
            </div>

            <!-- Search Radius Slider -->
            <div class="mb-6">
              <div class="text-subtitle-1 mb-2">
                Rayon de recherche:
                <span class="font-weight-bold text-primary"
                  >{{ searchRadius }} km</span
                >
              </div>
              <v-slider
                v-model="searchRadius"
                color="primary"
                track-color="blue-grey-lighten-4"
                :ticks="[5, 10, 15, 20]"
                show-ticks="always"
                step="5"
                min="5"
                max="20"
                thumb-label
              ></v-slider>
            </div>

            <!-- Submit Button -->
            <v-btn
              type="submit"
              color="primary"
              size="large"
              block
              elevation="2"
              prepend-icon="mdi-magnify"
              :loading="loading"
              :disabled="!selectedFilePath"
            >
              Visualiser / Analyser
            </v-btn>

            <!-- Error Display -->
            <v-alert
              v-if="errorMessage"
              type="error"
              variant="tonal"
              closable
              class="mt-4"
              @click:close="errorMessage = null"
            >
              {{ errorMessage }}
            </v-alert>
          </v-form>
        </div>
      </div>

      <!-- Vertical Resizer -->
      <div
        class="resizer-vertical"
        @mousedown="startDragSidebar"
        title="Redimensionner le panneau"
      ></div>

      <!-- Main Area: Map and Data Table -->
      <div class="d-flex flex-column flex-grow-1" style="min-width: 0;">
        <!-- Cartography Preview Area -->
        <div
          class="map-wrapper"
          :style="{ height: `${mapHeight}vh`, flexShrink: 0, position: 'relative', zIndex: 1 }"
        >
          <l-map
            ref="map"
            v-model:zoom="zoom"
            :center="center"
            :use-global-leaflet="false"
            @ready="onMapReady"
          >
            <l-tile-layer
              url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
              layer-type="base"
              name="OpenStreetMap"
              attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
            ></l-tile-layer>

            <!-- Ecological zones matching the spatial filter -->
            <l-geo-json
              v-for="zone in zonesGeoJson"
              :key="zone.id"
              :geojson="zone.geojson"
              :optionsStyle="() => ({
                color: zone.isInside ? '#FF9800' : '#4CAF50',
                weight: 2,
                dashArray: '5, 5',
                opacity: 0.8,
                fillColor: zone.isInside ? '#B3E5FC' : '#C8E6C9',
                fillOpacity: zone.isInside ? 0.6 : 0.5
              })"
            ></l-geo-json>

            <!-- Main study perimeter overlay -->
            <l-geo-json
              v-if="perimeterGeoJson"
              :geojson="perimeterGeoJson"
              :optionsStyle="() => ({
                color: '#F44336',
                weight: 3,
                fillColor: '#FFE0B2',
                fillOpacity: 0.4
              })"
            ></l-geo-json>

            <!-- Map Legend -->
            <l-control position="bottomleft">
              <v-card class="pa-3 elevation-3 rounded-lg" border="primary md" style="min-width: 220px; background-color: rgba(255, 255, 255, 0.95);">
                <div class="font-weight-bold mb-3 text-subtitle-2 text-primary d-flex align-center">
                  <v-icon size="small" start>mdi-map-legend</v-icon>
                  Légende
                </div>
                <!-- Perimeter -->
                <div class="d-flex align-center mb-2">
                  <div style="width: 16px; height: 16px; background-color: rgba(255, 224, 178, 0.4); border: 2px solid #F44336; margin-right: 14px; margin-left: 2px;"></div>
                  <span class="text-caption">Périmètre d'Étude</span>
                </div>
                <!-- Inside zones -->
                <div class="d-flex align-center mb-2">
                  <div style="width: 16px; height: 16px; background-color: rgba(179, 229, 252, 0.6); border: 2px dashed #FF9800; margin-right: 14px; margin-left: 2px;"></div>
                  <span class="text-caption">Dans le périmètre</span>
                </div>
                <!-- Outside zones -->
                <div class="d-flex align-center">
                  <div style="width: 16px; height: 16px; background-color: rgba(200, 230, 201, 0.5); border: 2px dashed #4CAF50; margin-right: 14px; margin-left: 2px;"></div>
                  <span class="text-caption">À proximité ({{ searchRadius }} km)</span>
                </div>
              </v-card>
            </l-control>
          </l-map>
        </div>

        <!-- Horizontal Resizer -->
        <div
          class="resizer-horizontal"
          @mousedown="startDragMap"
          title="Redimensionner la carte"
        ></div>

        <!-- Output Data Table Area -->
        <div
          class="data-table-container flex-grow-1 d-flex flex-column"
          style="min-height: 0; overflow: hidden;"
        >
          <v-tabs v-model="activeTab" color="primary" density="compact">
            <v-tab value="zones" prepend-icon="mdi-map-marker-radius">Zonages ({{ results.length }})</v-tab>
            <v-tab value="species" prepend-icon="mdi-bug">Espèces ({{ speciesResults.length }})</v-tab>
          </v-tabs>

          <v-window v-model="activeTab" class="flex-grow-1" style="min-height: 0;">
            <v-window-item value="zones" class="fill-height">
              <v-data-table
                :headers="headers"
                :items="results"
                :loading="loading"
                loading-text="Analyse spatiale en cours..."
                density="compact"
                hover
                height="100%"
                fixed-header
                :row-props="getRowProps"
              >
                <template v-slot:no-data>
                  <div class="pa-4 text-center text-medium-emphasis">
                    Aucun résultat à afficher. Veuillez lancer une analyse.
                  </div>
                </template>
              </v-data-table>
            </v-window-item>

            <v-window-item value="species" class="fill-height">
              <div class="d-flex flex-column fill-height">
                <v-text-field
                  v-model="speciesSearch"
                  placeholder="Filtrer les espèces (nom, zone, statut...)"
                  prepend-inner-icon="mdi-magnify"
                  variant="solo"
                  density="compact"
                  class="ma-2 flex-grow-0"
                  clearable
                  hide-details
                ></v-text-field>
                <v-data-table
                  :headers="speciesHeaders"
                  :items="speciesResults"
                  :search="speciesSearch"
                  :loading="loading"
                  density="compact"
                  hover
                  height="100%"
                  fixed-header
                  :sort-by="[{ key: 'endangermentScore', order: 'desc' }]"
                  class="flex-grow-1"
                >
                  <template v-slot:item.endangermentScore="{ value }">
                    <v-chip
                      :color="value >= 4 ? 'error' : value >= 3 ? 'warning' : 'primary'"
                      size="x-small"
                      variant="flat"
                    >
                      {{ value }}
                    </v-chip>
                  </template>
                  <template v-slot:no-data>
                    <div class="pa-4 text-center text-medium-emphasis">
                      Recherchez les espèces du périmètre en lançant l'analyse.
                    </div>
                  </template>
                </v-data-table>
              </div>
            </v-window-item>
          </v-window>
        </div>
      </div>
    </div>
  </v-container>
</template>

<style scoped>
.sidebar-container {
  background-color: rgb(var(--v-theme-surface));
  z-index: 2;
}

.data-table-container {
  background-color: rgb(var(--v-theme-background));
}

.resizer-vertical {
  width: 6px;
  cursor: col-resize;
  background-color: rgb(var(--v-theme-background));
  border-left: 1px solid rgba(var(--v-theme-on-surface), 0.12);
  border-right: 1px solid rgba(var(--v-theme-on-surface), 0.12);
  transition: background-color 0.2s;
  z-index: 3;
}
.resizer-vertical:hover, .resizer-vertical:active {
  background-color: rgba(var(--v-theme-primary), 0.3);
}

.resizer-horizontal {
  height: 6px;
  cursor: row-resize;
  background-color: rgb(var(--v-theme-background));
  border-top: 1px solid rgba(var(--v-theme-on-surface), 0.12);
  border-bottom: 1px solid rgba(var(--v-theme-on-surface), 0.12);
  transition: background-color 0.2s;
  z-index: 3;
}
.resizer-horizontal:hover, .resizer-horizontal:active {
  background-color: rgba(var(--v-theme-primary), 0.3);
}
</style>
