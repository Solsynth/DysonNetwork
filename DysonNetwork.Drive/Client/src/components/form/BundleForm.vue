<template>
  <n-form :model="formValue" :rules="rules" ref="formRef">
    <n-form-item label="Slug" path="slug">
      <n-input v-model:value="formValue.slug" placeholder="Input Slug" />
    </n-form-item>
    <n-form-item label="Name" path="name">
      <n-input v-model:value="formValue.name" placeholder="Input Name" />
    </n-form-item>
    <n-form-item label="Description" path="description">
      <n-input
        v-model:value="formValue.description"
        placeholder="Input Description"
        type="textarea"
      />
    </n-form-item>
    <n-form-item label="Passcode" path="passcode">
      <n-input
        v-model:value="formValue.passcode"
        placeholder="Input Passcode"
        type="password"
      />
    </n-form-item>
    <n-form-item label="Expired At" path="expiredAt">
      <n-date-picker v-model:value="formValue.expiredAt" type="datetime" />
    </n-form-item>
  </n-form>
</template>

<script lang="ts" setup>
import {
  NForm,
  NFormItem,
  NInput,
  NDatePicker,
  type FormInst,
  type FormRules,
} from 'naive-ui'
import { ref } from 'vue'

const formRef = ref<FormInst | null>(null)

const props = defineProps<{ value: any }>()
const formValue = ref(props.value)

const rules: FormRules = {
  slug: [
    {
      max: 1024,
      message: 'Slug can be at most 1024 characters long',
    },
  ],
  name: [
    {
      max: 1024,
      message: 'Name can be at most 1024 characters long',
    },
  ],
  description: [
    {
      max: 8192,
      message: 'Description can be at most 8192 characters long',
    },
  ],
  passcode: [
    {
      max: 256,
      message: 'Passcode can be at most 256 characters long',
    },
  ],
}

defineExpose({
  formRef,
})
</script>
