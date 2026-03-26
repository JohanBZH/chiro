<script setup>
import { ref, nextTick } from "vue";
import { LMap, LTileLayer, LGeoJson } from "@vue-leaflet/vue-leaflet";
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

// Data Table Setup
const headers = ref([
  { title: "Type de Zone", key: "type", align: "start" },
  { title: "Code ZNIEFF/N2000", key: "code" },
  { title: "Nom", key: "name" },
  { title: "Distance (km)", key: "distance" },
  { title: "Orientation", key: "orientation" },
]);
const results = ref([]);
const loading = ref(false);
const picking = ref(false);
const errorMessage = ref(null);

// Map Setup
const zoom = ref(6);
const center = ref([46.2276, 2.2137]); // Centered on France
let mapInstance = null;

const perimeterGeoJson = ref(null);
const zonesGeoJson = ref([]);

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
    <v-row no-gutters class="fill-height">
      <!-- Left Sidebar: Controls -->
      <v-col
        cols="12"
        md="3"
        class="sidebar-container fill-height d-flex flex-column elevation-4"
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
      </v-col>

      <!-- Main Area: Map and Data Table -->
      <v-col cols="12" md="9" class="d-flex flex-column fill-height pb-0 pt-0">
        <!-- Cartography Preview Area -->
        <div
          class="flex-grow-1"
          style="min-height: 50vh; position: relative; z-index: 1"
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
                color: zone.isInside ? '#FF5722' : '#2196F3',
                weight: 2,
                opacity: 0.8,
                fillColor: zone.isInside ? '#FFCCBC' : '#BBDEFB',
                fillOpacity: zone.isInside ? 0.6 : 0.2
              })"
            ></l-geo-json>

            <!-- Main study perimeter overlay -->
            <l-geo-json
              v-if="perimeterGeoJson"
              :geojson="perimeterGeoJson"
              :optionsStyle="() => ({
                color: '#D32F2F',
                weight: 3,
                dashArray: '5, 10',
                fillOpacity: 0
              })"
            ></l-geo-json>
          </l-map>
        </div>

        <v-divider></v-divider>

        <!-- Output Data Table Area -->
        <div
          class="data-table-container pb-4"
          style="height: 35vh; overflow-y: auto"
        >
          <v-card variant="flat" class="rounded-0">
            <v-card-title
              class="bg-surface text-primary pt-4 pb-2 text-subtitle-1 font-weight-bold"
            >
              <v-icon start icon="mdi-table" class="mr-2"></v-icon>
              Aperçu des Résultats (Zonages)
            </v-card-title>
            <v-card-text class="pa-0">
              <v-data-table
                :headers="headers"
                :items="results"
                :loading="loading"
                loading-text="Analyse spatiale en cours..."
                density="compact"
                hover
                :row-props="getRowProps"
              >
                <template v-slot:no-data>
                  <div class="pa-4 text-center text-medium-emphasis">
                    Aucun résultat à afficher. Veuillez lancer une analyse.
                  </div>
                </template>
              </v-data-table>
            </v-card-text>
          </v-card>
        </div>
      </v-col>
    </v-row>
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
</style>
