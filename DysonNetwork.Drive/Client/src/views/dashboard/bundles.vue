<template>
  <section class="h-full px-5 py-4">
    <n-data-table
      remote
      :row-key="(row) => row.id"
      :columns="tableColumns"
      :data="bundles"
      :loading="loading"
      :pagination="tablePagination"
      @page-change="handlePageChange"
    />
  </section>
</template>

<script lang="ts" setup>
import {
  NDataTable,
  type DataTableColumns,
  type PaginationProps,
  useMessage,
  useLoadingBar,
  NButton,
  NIcon,
  NSpace,
  useDialog,
} from 'naive-ui'
import { h, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { DeleteRound } from '@vicons/material'

const router = useRouter()

const bundles = ref<any[]>([])

const tableColumns: DataTableColumns<any> = [
  {
    title: 'Name',
    key: 'name',
    render(row: any) {
      return h(
        NButton,
        {
          text: true,
          onClick: () => {
            router.push(`/bundles/${row.id}`)
          },
        },
        {
          default: () => row.name,
        },
      )
    },
    maxWidth: 80,
  },
  {
    title: 'Description',
    key: 'description',
    maxWidth: 180,
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
  {
    title: 'Action',
    key: 'action',
    render(row: any) {
      return h(NSpace, {}, [
        h(
          NButton,
          {
            circle: true,
            text: true,
            type: 'error',
            onClick: () => {
              askDeleteBundle(row)
            },
          },
          {
            icon: () => h(NIcon, {}, { default: () => h(DeleteRound) }),
          },
        ),
      ])
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

async function fetchBundles() {
  if (loading.value) return
  try {
    loading.value = true
    const pag = tablePagination.value
    const response = await fetch(
      `/api/bundles/me?take=${pag.pageSize}&offset=${(pag.page! - 1) * pag.pageSize!}`,
    )
    if (!response.ok) {
      throw new Error('Network response was not ok')
    }
    const data = await response.json()
    bundles.value = data
    tablePagination.value.itemCount = parseInt(response.headers.get('x-total') ?? '0')
  } catch (error) {
    messageDialog.error('Failed to fetch bundles: ' + (error as Error).message)
    console.error('Failed to fetch bundles:', error)
  } finally {
    loading.value = false
  }
}
onMounted(() => fetchBundles())

function handlePageChange(page: number) {
  tablePagination.value.page = page
  fetchBundles()
}

const loading = ref(false)

const messageDialog = useMessage()
const loadingBar = useLoadingBar()
const dialog = useDialog()

function askDeleteBundle(bundle: any) {
  dialog.warning({
    title: 'Confirm',
    content: `Are you sure you want to delete the bundle ${bundle.name}?`,
    positiveText: 'Sure',
    negativeText: 'Not Sure',
    onPositiveClick: () => {
      deleteBundle(bundle)
    },
  })
}

async function deleteBundle(bundle: any) {
  try {
    loadingBar.start()
    const response = await fetch(`/api/bundles/${bundle.id}`, {
      method: 'DELETE',
    })
    if (!response.ok) {
      throw new Error('Network response was not ok')
    }
    tablePagination.value.page = 1
    await fetchBundles()
    loadingBar.finish()
    messageDialog.success('Bundle deleted successfully')
  } catch (error) {
    loadingBar.error()
    messageDialog.error('Failed to delete bundle: ' + (error as Error).message)
  }
}
</script>
