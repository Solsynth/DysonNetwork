<template>
  <n-select
    :options="pubStore.publishers"
    label-field="nick"
    value-field="name"
    :value="props.value"
    :render-label="renderLabel"
    :render-tag="renderSingleSelectTag"
    @update:value="(v) => emits('update:value', v)"
  />
</template>

<script setup lang="ts">
import { usePubStore } from '@/stores/pub'
import { NAvatar, NSelect, NText, type SelectRenderLabel, type SelectRenderTag } from 'naive-ui'
import { h, watch } from 'vue'

const pubStore = usePubStore()

const props = defineProps<{ value: string | undefined }>()
const emits = defineEmits(['update:value'])

watch(
  pubStore,
  (value) => {
    if (!props.value && value.publishers) {
      emits('update:value', pubStore.publishers[0].name)
    }
  },
  { deep: true, immediate: true },
)

const renderSingleSelectTag: SelectRenderTag = ({ option }: { option: any }) => {
  return h(
    'div',
    {
      style: {
        display: 'flex',
        alignItems: 'center',
      },
    },
    [
      h(NAvatar, {
        src: option.picture
          ? `/cgi/drive/files/${option.picture.id}`
          : undefined,
        round: true,
        size: 24,
        style: {
          marginRight: '8px',
        },
      }),
      option.nick as string,
    ],
  )
}

const renderLabel: SelectRenderLabel = (option: any) => {
  return h(
    'div',
    {
      style: {
        display: 'flex',
        alignItems: 'center',
      },
    },
    [
      h(NAvatar, {
        src: option.picture
          ? `/cgi/drive/files/${option.picture.id}`
          : undefined,
        round: true,
        size: 'small',
      }),
      h(
        'div',
        {
          style: {
            marginLeft: '8px',
            padding: '4px 0',
          },
        },
        [
          h('div', null, [option.nick as string]),
          h(
            NText,
            { depth: 3, tag: 'div' },
            {
              default: () => `@${option.name}`,
            },
          ),
        ],
      ),
    ],
  )
}
</script>
