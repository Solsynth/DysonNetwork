import { defineStore } from 'pinia'
import { ref, computed, watch } from 'vue'

export const useUserStore = defineStore('user', () => {
  // State
  const user = ref<any>(null)
  const isLoading = ref(false)
  const error = ref<string | null>(null)

  // Getters
  const isAuthenticated = computed(() => !!user.value)

  // Actions
  async function fetchUser(reload = true) {
    if (!reload && user.value) return // Skip fetching if already loaded and not forced to
    isLoading.value = true
    error.value = null
    try {
      const response = await fetch('/api/accounts/me', {
        credentials: 'include',
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

  watch(
    user,
    (_) => {
      // Broadcast user changes to other subapps
      window.parent.postMessage(
        {
          type: 'DY:LOGIN_STATUS_CHANGE',
          data: user.value != null,
        },
        '*',
      )
      console.log(`[SYNC] Message sent to parent: Login status changed to ${status}`)
    },
    { immediate: true, deep: true },
  )

  return {
    user,
    isLoading,
    error,
    isAuthenticated,
    fetchUser,
    logout,
  }
})
