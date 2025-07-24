import { createRouter, createWebHistory } from 'vue-router'
import { useUserStore } from '@/stores/user'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'index',
      component: () => import('../views/index.vue')
    },
    {
      path: '/captcha',
      name: 'captcha',
      component: () => import('../views/captcha.vue')
    },
    {
      path: '/spells/:word',
      name: 'spells',
      component: () => import('../views/spells.vue')
    },
    {
      path: '/login',
      name: 'login',
      component: () => import('../views/login.vue')
    },
    {
      path: '/create-account',
      name: 'create-account',
      component: () => import('../views/create-account.vue')
    },
    {
      path: '/accounts/me',
      name: 'me',
      component: () => import('../views/accounts/me.vue'),
      meta: { requiresAuth: true }
    }
  ]
})

router.beforeEach(async (to, from, next) => {
  const userStore = useUserStore()

  // Initialize user state if not already initialized
  if (!userStore.user && localStorage.getItem('authToken')) {
    await userStore.initialize()
  }

  if (to.matched.some((record) => record.meta.requiresAuth) && !userStore.isAuthenticated) {
    next({ name: 'login', query: { redirect: to.fullPath } })
  } else {
    next()
  }
})

export default router
