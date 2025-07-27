<template>
  <section class="h-full px-5 py-4">
    <n-data-table
      remote
      :row-key="(row) => row.id"
      :columns="tableColumns"
      :data="quotas"
      :loading="loading"
      :pagination="tablePagination"
      @page-change="handlePageChange"
    />
  </section>
</template>

<script lang="ts" setup>
import { NDataTable, type DataTableColumns, type PaginationProps, useMessage } from 'naive-ui'
import { onMounted, ref } from 'vue'
import { formatBytes } from '../format'

const quotas = ref<any[]>([])

const tableColumns: DataTableColumns<any> = [
  {
    title: 'Name',
    key: 'name',
  },
  {
    title: 'Description',
    key: 'description',
  },
  {
    title: 'Quota',
    key: 'quota',
    render(row: any) {
      return formatBytes(row.quota * 1024 * 1024)
    },
  },
  {
    title: 'Expired At',
    key: 'expired_at',
    render(row: any) {
      if (!row.expired_at) return 'Never'
      return new Date(row.expired_at).toLocaleString()
    },
  },
  {
    title: 'Created At',
    key: 'created_at',
    render(row: any) {
      return new Date(row.created_at).toLocaleString()
    },
  },
  {
    title: 'Updated At',
    key: 'updated_at',
    render(row: any) {
      return new Date(row.updated_at).toLocaleString()
    },
  },
]

const tablePagination = ref<PaginationProps>({
  page: 1,
  itemCount: 0,
  pageSize: 10,
  showSizePicker: true,
  pageSizes: [10, 20, 30, 40, 50],
})

async function fetchQuotas() {
  if (loading.value) return
  try {
    loading.value = true
    const pag = tablePagination.value
    const response = await fetch(
      `/api/billing/quota/records?take=${pag.pageSize}&offset=${(pag.page! - 1) * pag.pageSize!}`,
    )
    if (!response.ok) {
      throw new Error('Network response was not ok')
    }
    const data = await response.json()
    quotas.value = data
    tablePagination.value.itemCount = parseInt(response.headers.get('x-total') ?? '0')
  } catch (error) {
    messageDialog.error('Failed to fetch quotas: ' + (error as Error).message)
    console.error('Failed to fetch quotas:', error)
  } finally {
    loading.value = false
  }
}
onMounted(() => fetchQuotas())

function handlePageChange(page: number) {
  tablePagination.value.page = page
  fetchQuotas()
}

const loading = ref(false)

const messageDialog = useMessage()
</script>
