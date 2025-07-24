import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

export const useUserStore = defineStore('user', () => {
  // State
  const user = ref<any>(null)
  const isLoading = ref(false)
  const error = ref<string | null>(null)

  // Getters
  const isAuthenticated = computed(() => !!user.value)

  // Actions
  async function fetchUser() {
    const token = localStorage.getItem('authToken')
    if (!token) {
      return // No token, no need to fetch
    }

    isLoading.value = true
    error.value = null
    try {
      const response = await fetch('/api/accounts/me', {
        headers: {
          'Authorization': `Bearer ${token}`
        }
      })

      if (!response.ok) {
        // If the token is invalid, clear it and the user state
        if (response.status === 401) {
          logout()
        }
        throw new Error('Failed to fetch user information.')
      }

      user.value = await response.json()
    } catch (e: any) {
      error.value = e.message
      user.value = null // Clear user data on error
    } finally {
      isLoading.value = false
    }
  }

  function logout() {
    user.value = null
    localStorage.removeItem('authToken')
    // Optionally, redirect to login page
    // router.push('/login')
  }

  async function initialize() {
    await fetchUser()
  }

  return {
    user,
    isLoading,
    error,
    isAuthenticated,
    fetchUser,
    logout,
    initialize
  }
})