import { createRouter, createWebHistory } from 'vue-router'
import { useUserStore } from '@/stores/user'
import { useServicesStore } from '@/stores/services'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'index',
      component: () => import('../views/index.vue'),
    },
  ],
})

router.beforeEach(async (to, from, next) => {
  const userStore = useUserStore()
  const servicesStore = useServicesStore()

  // Initialize user state if not already initialized
  if (!userStore.user) {
    await userStore.fetchUser()
  }

  if (to.matched.some((record) => record.meta.requiresAuth) && !userStore.isAuthenticated) {
    window.open(
      servicesStore.getSerivceUrl(
        'DysonNetwork.Pass',
        'login?redirect=' + encodeURIComponent(window.location.href),
      )!,
      '_blank',
    )
    next('/')
  } else {
    next()
  }
})

export default router
