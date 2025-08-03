<template>
  <div>
    <milkdown />
  </div>
</template>

<script setup lang="ts">
import { Milkdown, useEditor } from "@milkdown/vue";
import { Crepe } from "@milkdown/crepe";
import "@milkdown/crepe/theme/common/style.css";
import "@milkdown/crepe/theme/frame.css";

useEditor((root) => {
  const crepe = new Crepe({
    root,
  });
  return crepe;
})
</script>
