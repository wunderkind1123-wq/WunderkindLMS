import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import { authApi } from '@/api/auth'
import { getToken, setToken } from '@/api/client'
import type { User } from '@/api/types'

interface AuthState {
  user: User | null
  loading: boolean
  login: (username: string, password: string) => Promise<void>
  logout: () => void
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null)
  const [loading, setLoading] = useState(Boolean(getToken()) || Boolean(readSsoToken()))

  useEffect(() => {
    // Opened from LMS: exchange its one-time token for our session, then drop it from the address.
    const sso = readSsoToken()
    if (sso) {
      window.history.replaceState(null, '', window.location.pathname + window.location.search)
      authApi
        .sso(sso)
        .then((res) => {
          setToken(res.access_token)
          setUser(res.user)
        })
        .catch(() => setToken(null))
        .finally(() => setLoading(false))
      return
    }
    if (!getToken()) return
    authApi
      .me()
      .then(setUser)
      .catch(() => setToken(null))
      .finally(() => setLoading(false))
  }, [])

  const login = useCallback(async (username: string, password: string) => {
    const res = await authApi.login(username, password)
    setToken(res.access_token)
    setUser(res.user)
  }, [])

  const logout = useCallback(() => {
    setToken(null)
    setUser(null)
  }, [])

  return (
    <AuthContext.Provider value={{ user, loading, login, logout }}>{children}</AuthContext.Provider>
  )
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}

export const isAdmin = (user: User | null) => user?.role === 'admin'

function readSsoToken(): string | null {
  const m = /[#&]sso=([^&]+)/.exec(window.location.hash)
  return m ? decodeURIComponent(m[1]) : null
}
