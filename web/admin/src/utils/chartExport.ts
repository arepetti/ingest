/**
 * Best-effort "save this chart as a PNG" for the recharts SVG surfaces on the Explore page.
 * Rasterises the live <svg> onto a canvas and triggers a download. Kept dependency-free; it
 * inlines a white background and a sans-serif font so the exported image is legible on its own
 * (the on-screen theme colours are carried by the SVG's own attributes).
 */

/** Find the recharts surface inside a container and export it as a PNG download. */
export async function exportChartPng(container: HTMLElement | null, filename: string, scale = 2): Promise<void> {
  const svg = container?.querySelector('svg')
  if (!svg) throw new Error('No chart to export yet.')
  await exportSvgAsPng(svg, filename, scale)
}

async function exportSvgAsPng(svg: SVGSVGElement, filename: string, scale: number): Promise<void> {
  const rect = svg.getBoundingClientRect()
  const width = Math.max(1, Math.round(rect.width))
  const height = Math.max(1, Math.round(rect.height))

  const clone = svg.cloneNode(true) as SVGSVGElement
  clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg')
  clone.setAttribute('width', String(width))
  clone.setAttribute('height', String(height))
  // A neutral font + white backdrop so the standalone file doesn't depend on the page's CSS.
  clone.style.fontFamily = 'Segoe UI, system-ui, sans-serif'
  const bg = document.createElementNS('http://www.w3.org/2000/svg', 'rect')
  bg.setAttribute('width', String(width))
  bg.setAttribute('height', String(height))
  bg.setAttribute('fill', '#ffffff')
  clone.insertBefore(bg, clone.firstChild)

  const svgText = new XMLSerializer().serializeToString(clone)
  const svgUrl = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svgText)}`

  const img = new Image()
  img.width = width
  img.height = height
  await new Promise<void>((resolve, reject) => {
    img.onload = () => resolve()
    img.onerror = () => reject(new Error('Could not render the chart image.'))
    img.src = svgUrl
  })

  const canvas = document.createElement('canvas')
  canvas.width = width * scale
  canvas.height = height * scale
  const ctx = canvas.getContext('2d')
  if (!ctx) throw new Error('Canvas is not supported in this browser.')
  ctx.scale(scale, scale)
  ctx.drawImage(img, 0, 0, width, height)

  const blob: Blob | null = await new Promise(resolve => canvas.toBlob(resolve, 'image/png'))
  if (!blob) throw new Error('Could not encode the chart image.')

  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  setTimeout(() => URL.revokeObjectURL(url), 0)
}
