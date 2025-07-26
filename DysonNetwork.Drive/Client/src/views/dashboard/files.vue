<template>
  <section class="h-full px-5 py-4">
    <n-data-table :columns="tableColumns" :data="files" :pagination="tablePagination" />
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
} from 'naive-ui'
import {
  AudioFileRound,
  InsertDriveFileRound,
  VideoFileRound,
  FileDownloadOutlined,
  DeleteRound,
} from '@vicons/material'
import { h, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { formatBytes } from '../format'

const router = useRouter()

const files = ref<any[]>([])

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
            quaternary: true,
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
            quaternary: true,
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
})

async function fetchFiles() {
  try {
    const response = await fetch('/api/files/me')
    if (!response.ok) {
      throw new Error('Network response was not ok')
    }
    const data = await response.json()
    files.value = data
    tablePagination.value.itemCount = parseInt(response.headers.get('x-total') ?? '0')
  } catch (error) {
    console.error('Failed to fetch files:', error)
  }
}
onMounted(() => fetchFiles())

const dialog = useDialog()
const messageDialog = useMessage()
const loadingBar = useLoadingBar()

async function askDeleteFile(file: any) {
  dialog.warning({
    title: 'Confirm',
    content: `Are you sure you want delete ${file.name}?`,
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
</script>
