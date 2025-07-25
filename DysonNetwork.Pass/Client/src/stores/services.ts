import { defineStore } from 'pinia'
import { ref } from 'vue'

export const useServicesStore = defineStore('services', () => {
  const services = ref<Record<string, string>>({})

  async function fetchServices() {
    try {
      const response = await fetch('/cgi/.well-known/services')
      if (!response.ok) {
        throw new Error('Network response was not ok')
      }
      const data = await response.json()
      services.value = data
    } catch (error) {
      console.error('Failed to fetch services:', error)
      services.value = {}
    }
  }

  return { services, fetchServices }
})
