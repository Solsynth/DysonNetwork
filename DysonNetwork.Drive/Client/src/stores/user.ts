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
    isLoading.value = true
    error.value = null
    try {
      const response = await fetch('/cgi/id/accounts/me', {
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

  function initialize() {
    const allowedOrigin = import.meta.env.DEV ? window.location.origin : 'https://id.solian.app'
    window.addEventListener('message', (event) => {
      // IMPORTANT: Always check the origin of the message for security!
      // This prevents malicious scripts from sending fake login status updates.
      // Ensure event.origin exactly matches your identity service's origin.
      if (event.origin !== allowedOrigin) {
        console.warn(`[SYNC] Message received from unexpected origin: ${event.origin}. Ignoring.`)
        return // Ignore messages from unknown origins
      }

      // Check if the message is the type we're expecting
      if (event.data && event.data.type === 'DY:LOGIN_STATUS_CHANGE') {
        const { loggedIn } = event.data
        console.log(`[SYNC] Received login status change: ${loggedIn}`)
        fetchUser() // Re-fetch user data on login status change
      }
    })
  }

  return {
    user,
    isLoading,
    error,
    isAuthenticated,
    fetchUser,
    logout,
    initialize,
  }
})
