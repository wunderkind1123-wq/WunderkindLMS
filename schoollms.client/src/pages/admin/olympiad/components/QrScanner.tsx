import { useEffect, useRef, useState } from 'react'
import { Camera, CameraOff } from 'lucide-react'
import jsQR from 'jsqr'
import { Button } from '@/components/ui/Button'

interface Detector {
  detect: (source: CanvasImageSource) => Promise<{ rawValue: string }[]>
}
type DetectorCtor = new (opts: { formats: string[] }) => Detector

/**
 * Telefon kamerasi bilan QR o'qish. Brauzerda `BarcodeDetector` bo'lsa — u (Chrome/Android), aks holda
 * `jsqr` (iOS Safari). Kamera faqat HTTPS (yoki localhost) da ishlaydi.
 * Bir xil kod 3 soniya ichida qayta yuborilmaydi.
 */
export function QrScanner({ onScan, paused }: { onScan: (text: string) => void; paused?: boolean }) {
  const video = useRef<HTMLVideoElement>(null)
  const canvas = useRef<HTMLCanvasElement>(null)
  const [on, setOn] = useState(false)
  const [error, setError] = useState('')
  const last = useRef<{ text: string; at: number }>({ text: '', at: 0 })
  const cb = useRef(onScan)
  const pausedRef = useRef(paused)
  useEffect(() => {
    cb.current = onScan
    pausedRef.current = paused
  }, [onScan, paused])

  useEffect(() => {
    if (!on) return
    let stream: MediaStream | null = null
    let raf = 0
    let stopped = false
    const Ctor = (window as unknown as { BarcodeDetector?: DetectorCtor }).BarcodeDetector
    const detector = Ctor ? new Ctor({ formats: ['qr_code'] }) : null

    const emit = (text: string) => {
      const now = Date.now()
      if (pausedRef.current) return
      if (text === last.current.text && now - last.current.at < 3000) return
      last.current = { text, at: now }
      navigator.vibrate?.(80)
      cb.current(text)
    }

    const tick = async () => {
      if (stopped) return
      const v = video.current
      if (v && v.readyState >= 2) {
        try {
          if (detector) {
            const codes = await detector.detect(v)
            if (codes[0]?.rawValue) emit(codes[0].rawValue)
          } else if (canvas.current) {
            const c = canvas.current
            const w = 480
            const h = Math.round((v.videoHeight / v.videoWidth) * w) || 360
            c.width = w
            c.height = h
            const ctx = c.getContext('2d', { willReadFrequently: true })
            if (ctx) {
              ctx.drawImage(v, 0, 0, w, h)
              const img = ctx.getImageData(0, 0, w, h)
              const code = jsQR(img.data, w, h, { inversionAttempts: 'dontInvert' })
              if (code?.data) emit(code.data)
            }
          }
        } catch {
          /* bitta kadr o'qilmadi — keyingisi */
        }
      }
      raf = window.setTimeout(tick, 150)
    }

    navigator.mediaDevices
      ?.getUserMedia({ video: { facingMode: 'environment' }, audio: false })
      .then((s) => {
        if (stopped) {
          s.getTracks().forEach((t) => t.stop())
          return
        }
        stream = s
        if (video.current) {
          video.current.srcObject = s
          void video.current.play()
        }
        tick()
      })
      .catch(() => {
        setError("Kameraga ruxsat berilmadi yoki kamera yo'q. Brauzer sozlamasida ruxsat bering (sayt HTTPS bo'lishi kerak).")
        setOn(false)
      })
    return () => {
      stopped = true
      clearTimeout(raf)
      stream?.getTracks().forEach((t) => t.stop())
    }
  }, [on])

  return (
    <div className="space-y-2">
      {on ? (
        <div className="relative overflow-hidden rounded-2xl bg-black">
          <video ref={video} playsInline muted className="aspect-square w-full object-cover sm:aspect-video" />
          <div className="pointer-events-none absolute inset-0 flex items-center justify-center">
            <div className="h-1/2 w-1/2 max-w-64 rounded-2xl border-4 border-white/80 sm:h-2/3" />
          </div>
          <canvas ref={canvas} className="hidden" />
        </div>
      ) : null}
      <Button
        variant={on ? 'secondary' : 'primary'}
        className="w-full justify-center py-3"
        onClick={() => {
          if (!on && !navigator.mediaDevices) {
            setError('Bu brauzerda kamera ishlamaydi (HTTPS kerak).')
            return
          }
          setError('')
          setOn((v) => !v)
        }}
      >
        {on ? <CameraOff className="h-5 w-5" /> : <Camera className="h-5 w-5" />}
        {on ? "Kamerani o'chirish" : 'QR skanerni yoqish'}
      </Button>
      {error && <p className="text-sm text-red-600">{error}</p>}
    </div>
  )
}
