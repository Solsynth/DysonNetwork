<template>
  <section class="h-full container-fluid mx-auto py-4 px-5">
    <div class="h-full flex justify-center items-center" v-if="!usage">
      <n-spin />
    </div>
    <n-grid cols="1 s:2 l:4" responsive="screen" :x-gap="16" :y-gap="16" v-else>
      <n-gi span="4">
        <n-alert title="Billing Tips" size="small" type="info" closable>
          <p>
            The minimal billable unit is MiB, if your file is not enough 1 MiB it will be counted as
            1 MiB.
          </p>
          <p>The <b>1 MiB = 1024 KiB = 1,048,576 B</b></p>
        </n-alert>
      </n-gi>
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
          <n-statistic label="Quota" tabular-nums>
            <n-number-animation :from="0" :to="usage.total_quota" />
            <template #suffix>MiB</template>
          </n-statistic>
        </n-card>
      </n-gi>
      <n-gi>
        <n-card class="h-stats">
          <div class="flex gap-2 justify-between items-end">
            <n-statistic label="Used Quota" tabular-nums>
              <n-number-animation :from="0" :to="quotaUsagePercentage" :precision="2" />
              <template #suffix>%</template>
            </n-statistic>
            <n-progress
              type="circle"
              :percentage="quotaUsagePercentage"
              :show-indicator="false"
              :stroke-width="16"
              style="width: 40px"
            />
          </div>
        </n-card>
      </n-gi>
      <n-gi span="2">
        <n-card class="aspect-video" title="Pool Usage">
          <pie
            :data="poolChartData"
            :options="{
              maintainAspectRatio: false,
              responsive: true,
              plugins: { legend: { position: isDesktop ? 'right' : 'bottom' } },
            }"
          />
        </n-card>
      </n-gi>
      <n-gi span="2">
        <n-card class="aspect-video h-full" title="Verbose Quota">
          <pie
            :data="quotaChartData"
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
import { NSpin, NCard, NStatistic, NGrid, NGi, NNumberAnimation, NAlert, NProgress } from 'naive-ui'
import { Chart as ChartJS, Title, Tooltip, Legend, ArcElement } from 'chart.js'
import { Pie } from 'vue-chartjs'
import { computed, onMounted, ref } from 'vue'
import { breakpointsTailwind, useBreakpoints } from '@vueuse/core'

ChartJS.register(Title, Tooltip, Legend, ArcElement)

const breakpoints = useBreakpoints(breakpointsTailwind)
const isDesktop = breakpoints.greaterOrEqual('md')

const poolChartData = computed(() => ({
  labels: usage.value.pool_usages.map((pool: any) => pool.pool_name),
  datasets: [
    {
      label: 'Pool Usage',
      backgroundColor: '#7D80BAFF',
      data: usage.value.pool_usages.map((pool: any) => pool.usage_bytes),
    },
  ],
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

const verboseQuota = ref<
  { based_quota: number; extra_quota: number; total_quota: number } | undefined
>()
async function fetchVerboseQuota() {
  try {
    const response = await fetch('/api/billing/quota')
    if (!response.ok) {
      throw new Error('Network response was not ok')
    }
    verboseQuota.value = await response.json()
  } catch (error) {
    console.error('Failed to fetch verbose data:', error)
  }
}
onMounted(() => fetchVerboseQuota())

const quotaChartData = computed(() => ({
  labels: ['Base Quota', 'Extra Quota'],
  datasets: [
    {
      label: 'Verbose Quota',
      backgroundColor: '#7D80BAFF',
      data: [verboseQuota.value?.based_quota ?? 0, verboseQuota.value?.extra_quota ?? 0],
    },
  ],
}))
const quotaUsagePercentage = computed(
  () => (usage.value.used_quota / usage.value.total_quota) * 100,
)

function toGigabytes(bytes: number): number {
  return bytes / (1024 * 1024 * 1024)
}
</script>

<style scoped>
.h-stats {
  height: 105px;
}
</style>
