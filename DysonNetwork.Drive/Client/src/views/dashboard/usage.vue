<template>
  <section class="h-full container-fluid mx-auto py-4 px-5">
    <div class="h-full flex justify-center items-center" v-if="!usage">
      <n-spin />
    </div>
    <n-grid cols="1 s:2 m:3 l:4" responsive="screen" :x-gap="16" :y-gap="16" v-else>
      <n-gi>
        <n-card class="h-stats">
          <n-statistic label="All Uploads" tabular-nums>
            <n-number-animation
              :from="0"
              :to="toGigabytes(usage.total_usage_bytes)"
              :precision="3"
            />
            <template #suffix>GiB</template>
          </n-statistic>
        </n-card>
      </n-gi>
      <n-gi>
        <n-card class="h-stats">
          <n-statistic label="All Files" tabular-nums>
            <n-number-animation :from="0" :to="usage.total_file_count" />
          </n-statistic>
        </n-card>
      </n-gi>
      <n-gi>
        <n-card class="h-stats">
          <n-statistic label="Cost" tabular-nums>
            <n-number-animation :from="0" :to="usage.total_cost" :precision="2" />
            <template #suffix>NSD</template>
          </n-statistic>
        </n-card>
      </n-gi>
      <n-gi>
        <n-card class="h-stats">
          <n-statistic label="Pools" tabular-nums>
            <n-number-animation :from="0" :to="usage.pool_usages.length" />
          </n-statistic>
        </n-card>
      </n-gi>
      <n-gi span="2">
        <n-card class="ratio-video" title="Pool Usage">
          <pie
            :data="chartData"
            :options="{
              maintainAspectRatio: false,
              responsive: true,
              plugins: { legend: { position: isDesktop ? 'right' : 'bottom' } },
            }"
          />
        </n-card>
      </n-gi>
    </n-grid>
  </section>
</template>

<script setup lang="ts">
import { NSpin, NCard, NStatistic, NGrid, NGi, NNumberAnimation } from 'naive-ui'
import { Chart as ChartJS, Title, Tooltip, Legend, ArcElement } from 'chart.js'
import { Pie } from 'vue-chartjs'
import { computed, onMounted, ref } from 'vue'
import { breakpointsTailwind, useBreakpoints } from '@vueuse/core'

ChartJS.register(Title, Tooltip, Legend, ArcElement)

const breakpoints = useBreakpoints(breakpointsTailwind)
const isDesktop = breakpoints.greaterOrEqual('md')

const chartData = computed(() => ({
  labels: usage.value.pool_usages.map((pool: any) => pool.pool_name),
  datasets: [
    {
      label: 'Pool Usage',
      backgroundColor: '#7D80BAFF',
      data: usage.value.pool_usages.map((pool: any) => pool.usage_bytes),
    },
  ]
}))

const usage = ref<any>()
async function fetchUsage() {
  try {
    const response = await fetch('/api/billing/usage')
    if (!response.ok) {
      throw new Error('Network response was not ok')
    }
    usage.value = await response.json()
  } catch (error) {
    console.error('Failed to fetch usage data:', error)
  }
}
onMounted(() => fetchUsage())

function toGigabytes(bytes: number): number {
  return bytes / (1024 * 1024 * 1024)
}
</script>

<style scoped>
.h-stats {
  height: 105px;
}
</style>
