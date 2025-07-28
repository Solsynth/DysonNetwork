<template>
  <section class="h-full px-5 py-4">
    <div class="flex items-center gap-4 mb-3">
      <file-pool-select
        v-model="filePool"
        placeholder="Filter by file pool"
        size="medium"
        class="max-w-[480px]"
        @update:pool="fetchFiles"
      />
      <div class="flex items-center gap-2.5">
        <n-switch size="large" v-model:value="showRecycled">
          <template #checked>Recycled</template>
          <template #unchecked>Unrecycled</template>
        </n-switch>
        <n-button
          @click="askDeleteRecycledFiles"
          v-if="showRecycled"
          type="error"
          circle
          size="small"
        >
          <n-icon>
            <delete-sweep-round />
          </n-icon>
        </n-button>
      </div>
    </div>
    <n-data-table
      remote
      :row-key="(row) => row.id"
      :columns="tableColumns"
      :data="files"
      :loading="loading"
      :pagination="tablePagination"
      @page-change="handlePageChange"
    />
  </section>
</template>

<script lang="ts" setup>
import {
  NDataTable,
  NIcon,
  NImage,
  NButton,
  NSpace,
  type DataTableColumns,
  type PaginationProps,
  useDialog,
  useMessage,
  useLoadingBar,
  NSwitch,
  NTooltip,
} from 'naive-ui'
import {
  AudioFileRound,
  InsertDriveFileRound,
  VideoFileRound,
  FileDownloadOutlined,
  DeleteRound,
  DeleteSweepRound,
} from '@vicons/material'
import { h, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { formatBytes } from '../format'
import FilePoolSelect from '@/components/FilePoolSelect.vue'

const router = useRouter()

const files = ref<any[]>([])

const filePool = ref<string | null>(null)
const showRecycled = ref(false)

const tableColumns: DataTableColumns<any> = [
  {
    title: 'Preview',
    key: 'preview',
    render(row: any) {
      switch (row.mime_type.split('/')[0]) {
        case 'image':
          return h(NImage, {
            src: '/api/files/' + row.id,
            width: 32,
            height: 32,
            objectFit: 'contain',
            style: { aspectRatio: 1 },
          })
        case 'video':
          return h(NIcon, { size: 32 }, { default: () => h(VideoFileRound) })
        case 'audio':
          return h(NIcon, { size: 32 }, { default: () => h(AudioFileRound) })
        default:
          return h(NIcon, { size: 32 }, { default: () => h(InsertDriveFileRound) })
      }
    },
  },
  {
    title: 'Name',
    key: 'name',
    maxWidth: 180,
    ellipsis: true,
    render(row: any) {
      return h(
        NButton,
        {
          text: true,
          onClick: () => {
            router.push(`/files/${row.id}`)
          },
        },
        {
          default: () => row.name,
        },
      )
    },
  },
  {
    title: 'Size',
    key: 'size',
    render(row: any) {
      return formatBytes(row.size)
    },
  },
  {
    title: 'Pool',
    key: 'pool',
    render(row: any) {
      if (!row.pool) return 'Unstored'
      return h(
        NTooltip,
        {},
        {
          default: () => h('span', row.pool.id),
          trigger: () => h('span', row.pool.name),
        },
      )
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
    title: 'Uploaded At',
    key: 'created_at',
    render(row: any) {
      return new Date(row.created_at).toLocaleString()
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
            onClick: () => {
              window.open(`/api/files/${row.id}`, '_blank')
            },
          },
          {
            icon: () => h(NIcon, {}, { default: () => h(FileDownloadOutlined) }),
          },
        ),
        h(
          NButton,
          {
            circle: true,
            text: true,
            type: 'error',
            onClick: () => {
              askDeleteFile(row)
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

async function fetchFiles() {
  if (loading.value) return
  try {
    loading.value = true
    const pag = tablePagination.value
    const response = await fetch(
      `/api/files/me?take=${pag.pageSize}&offset=${(pag.page! - 1) * pag.pageSize!}&recycled=${showRecycled.value}${filePool.value ? '&pool=' + filePool.value : ''}`,
    )
    if (!response.ok) {
      throw new Error('Network response was not ok')
    }
    const data = await response.json()
    files.value = data
    tablePagination.value.itemCount = parseInt(response.headers.get('x-total') ?? '0')
  } catch (error) {
    console.error('Failed to fetch files:', error)
  } finally {
    loading.value = false
  }
}
onMounted(() => fetchFiles())

watch(showRecycled, () => {
  tablePagination.value.itemCount = 0
  tablePagination.value.page = 1
  fetchFiles()
})

function handlePageChange(page: number) {
  tablePagination.value.page = page
  fetchFiles()
}

const loading = ref(false)

const dialog = useDialog()
const messageDialog = useMessage()
const loadingBar = useLoadingBar()

function askDeleteFile(file: any) {
  dialog.warning({
    title: 'Confirm',
    content: `Are you sure you want delete ${file.name}? This will delete the stored file data immediately, there is no return.`,
    positiveText: 'Sure',
    negativeText: 'Not Sure',
    draggable: true,
    onPositiveClick: () => {
      deleteFile(file)
    },
  })
}

async function deleteFile(file: any) {
  try {
    loadingBar.start()
    const response = await fetch(`/api/files/${file.id}`, {
      method: 'DELETE',
    })
    if (!response.ok) {
      throw new Error('Network response was not ok')
    }
    tablePagination.value.page = 1
    await fetchFiles()
    loadingBar.finish()
    messageDialog.success('File deleted successfully')
  } catch (error) {
    loadingBar.error()
    messageDialog.error('Failed to delete file: ' + (error as Error).message)
  }
}

function askDeleteRecycledFiles() {
  dialog.warning({
    title: 'Confirm',
    content: `Are you sure you want to delete all ${tablePagination.value.itemCount} marked recycled file(s) by system?`,
    positiveText: 'Sure',
    negativeText: 'Not Sure',
    draggable: true,
    onPositiveClick: () => {
      deleteRecycledFiles()
    },
  })
}

async function deleteRecycledFiles() {
  try {
    loadingBar.start()
    const response = await fetch('/api/files/me/recycle', {
      method: 'DELETE',
    })
    if (!response.ok) {
      throw new Error('Network response was not ok')
    }
    const resp = await response.json()
    tablePagination.value.page = 1
    await fetchFiles()
    loadingBar.finish()
    messageDialog.success(`Recycled files deleted successfully, deleted count: ${resp.count}`)
  } catch (error) {
    loadingBar.error()
    messageDialog.error('Failed to delete recycled files: ' + (error as Error).message)
  }
}
</script>
