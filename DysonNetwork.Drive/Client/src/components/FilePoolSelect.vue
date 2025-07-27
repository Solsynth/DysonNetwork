<template>
  <n-select
    :value="modelValue"
    @update:value="onUpdate"
    :options="pools ?? []"
    :render-label="renderPoolSelectLabel"
    :render-tag="renderSingleSelectTag"
    value-field="id"
    label-field="name"
    :placeholder="props.placeholder || 'Select a file pool to upload'"
    :size="props.size || 'large'"
    clearable
  />
</template>

<script setup lang="ts">
import {
  NSelect,
  NTag,
  NDivider,
  NTooltip,
  type SelectOption,
  type SelectRenderTag,
} from 'naive-ui'
import { h, onMounted, ref, watch } from 'vue'
import type { SnFilePool } from '@/types/pool'
import { formatBytes } from '@/views/format'

const props = defineProps<{
  modelValue: string | null
  placeholder?: string | undefined
  size?: 'tiny' | 'small' | 'medium' | 'large' | undefined
}>()

const emit = defineEmits(['update:modelValue', 'update:pool'])

type SnFilePoolOption = SnFilePool & any

const pools = ref<SnFilePoolOption[] | undefined>()
async function fetchPools() {
  const resp = await fetch('/api/pools')
  pools.value = await resp.json()
}
onMounted(() => fetchPools())

function onUpdate(value: string | null) {
  emit('update:modelValue', value)
  if (value === null) {
    emit('update:pool', null)
    return
  }
  if (pools.value) {
    const pool = pools.value.find((p) => p.id === value) ?? null
    emit('update:pool', pool)
  }
}

watch(pools, (newPools) => {
  if (props.modelValue && newPools) {
    const pool = newPools.find((p) => p.id === props.modelValue) ?? null
    emit('update:pool', pool)
  }
})

const renderSingleSelectTag: SelectRenderTag = ({ option }) => {
  return h(
    'div',
    {
      style: {
        display: 'flex',
        alignItems: 'center',
      },
    },
    [option.name as string],
  )
}

const perkPrivilegeList = ['Stellar', 'Nova', 'Supernova']

function renderPoolSelectLabel(option: SelectOption & SnFilePool) {
  const policy: any = option.policy_config
  return h(
    'div',
    {
      style: {
        padding: '8px 2px',
      },
    },
    [
      h('div', null, [option.name as string]),
      option.description &&
        h(
          'div',
          {
            style: {
              fontSize: '0.875rem',
              opacity: '0.75',
            },
          },
          option.description,
        ),
      h(
        'div',
        {
          style: {
            display: 'flex',
            marginBottom: '4px',
            fontSize: '0.75rem',
            opacity: '0.75',
          },
        },
        [
          policy.max_file_size && h('span', `Max ${formatBytes(policy.max_file_size)}`),
          policy.accept_types &&
            h(
              NTooltip,
              {},
              {
                trigger: () => h('span', `Accept limited types`),
                default: () => h('span', policy.accept_types.join(', ')),
              },
            ),
          policy.require_privilege &&
            h('span', `Require ${perkPrivilegeList[policy.require_privilege - 1]} Program`),
          h('span', `Cost x${option.billing_config.cost_multiplier.toFixed(1)}`),
        ]
          .filter((el) => el)
          .flatMap((el, idx, arr) =>
            idx < arr.length - 1 ? [el, h(NDivider, { vertical: true })] : [el],
          ),
      ),
      h(
        'div',
        {
          style: {
            display: 'flex',
            gap: '0.25rem',
            marginTop: '2px',
            marginLeft: '-2px',
            marginRight: '-2px',
          },
        },
        [
          policy.public_usable &&
            h(
              NTag,
              {
                type: 'info',
                size: 'small',
                round: true,
              },
              { default: () => 'Public Shared' },
            ),
          policy.public_indexable &&
            h(
              NTag,
              {
                type: 'success',
                size: 'small',
                round: true,
              },
              { default: () => 'Public Indexable' },
            ),
          policy.allow_encryption &&
            h(
              NTag,
              {
                type: 'warning',
                size: 'small',
                round: true,
              },
              { default: () => 'Allow Encryption' },
            ),
          policy.allow_anonymous &&
            h(
              NTag,
              {
                type: 'info',
                size: 'small',
                round: true,
              },
              { default: () => 'Allow Anonymous' },
            ),
          policy.enable_recycle &&
            h(
              NTag,
              {
                type: 'info',
                size: 'small',
                round: true,
              },
              { default: () => 'Recycle Enabled' },
            ),
        ],
      ),
    ],
  )
}
</script>
