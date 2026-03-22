<script setup>
import { ref } from "vue";
import { LMap, LTileLayer } from "@vue-leaflet/vue-leaflet";
import { sendMessageToBackend } from "../services/photinoService";

// Perimeter Input
const perimeterFile = ref(null);

// Search Radius (Intervals: 5, 10, 15, 20)
const searchRadius = ref(20);

// Data Table Setup (Placeholder for project 1 outputs)
const headers = ref([
  { title: "Type de Zone", key: "type", align: "start" },
  { title: "Code ZNIEFF/N2000", key: "code" },
  { title: "Nom", key: "name" },
  { title: "Distance (km)", key: "distance" },
  { title: "Orientation", key: "orientation" },
]);
const results = ref([]);
const loading = ref(false);

// Map Setup
const zoom = ref(6);
const center = ref([46.2276, 2.2137]); // Centered on France

// Handle Submission
const handleSubmit = async () => {
  loading.value = true;

  try {
    const payload = {
      radius: searchRadius.value,
      filename: perimeterFile.value ? perimeterFile.value.name : null,
    };

    // Call the Photino backend (C#)
    const response = await sendMessageToBackend("ProcessPerimeter", payload);
    console.log("Received from backend:", response);
  } catch (error) {
    console.error("Error contacting backend:", error);
  } finally {
    loading.value = false;
  }
};

// Ensure the Map resizes correctly after Vuetify layout completion
const onMapReady = (mapObject) => {
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
            <!-- Perimeter Upload -->
            <v-file-input
              v-model="perimeterFile"
              label="Périmètre d'étude (.shp, .csv)"
              accept=".shp,.csv,.kml,.gpx"
              prepend-icon="mdi-map-marker-path"
              variant="outlined"
              color="primary"
              hint="Importez votre zone d'étude"
              persistent-hint
              class="mb-6"
            ></v-file-input>

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
            >
              Visualiser / Analyser
            </v-btn>
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
