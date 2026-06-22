/**
 * Geometry for the Red/Amber/Green (RAG) target band overlaid on KPI charts.
 *
 * A value carries up to four nullable edges defining two nested ranges on the Y axis:
 * the acceptable (amber) range `amberMin..amberMax` and the ideal (green) range
 * `greenMin..greenMax` sitting inside it. The server guarantees the edges are coherent
 * (`amberMin ≤ greenMin ≤ greenMax ≤ amberMax`, each optional, green needs amber on its side),
 * so this helper only turns them into the rectangles a chart should shade. Anything outside the
 * amber range is "red" and simply left unshaded.
 */
export interface RagBandEdges {
  greenMin?: number | null
  greenMax?: number | null
  amberMin?: number | null
  amberMax?: number | null
}

/** A single shaded rectangle. `y1`/`y2` left undefined mean "anchor to the axis extent". */
export interface RagBandRect {
  key: string
  tone: 'green' | 'amber'
  y1?: number
  y2?: number
}

/**
 * Turn a value's RAG edges into the (0–3) rectangles to shade, painted from outer to inner so a
 * caller can render them in array order without worrying about overlap.
 */
export function ragBandRects(edges: RagBandEdges): RagBandRect[] {
  const greenMin = edges.greenMin ?? null
  const greenMax = edges.greenMax ?? null
  const amberMin = edges.amberMin ?? null
  const amberMax = edges.amberMax ?? null

  const hasGreen = greenMin !== null || greenMax !== null
  const hasAmber = amberMin !== null || amberMax !== null
  if (!hasGreen && !hasAmber) return []

  // Amber-only: the whole acceptable range is amber, with no finer ideal sub-range.
  if (!hasGreen) {
    return [{ key: 'amber', tone: 'amber', y1: amberMin ?? undefined, y2: amberMax ?? undefined }]
  }

  const rects: RagBandRect[] = []

  // Amber shoulders: the gap between the acceptable edge and the ideal edge on each side. They
  // only exist when both edges are present and actually leave room (a missing green edge means
  // the green zone reaches the acceptable edge, so there's no shoulder there).
  if (amberMin !== null && greenMin !== null && greenMin > amberMin)
    rects.push({ key: 'amber-lo', tone: 'amber', y1: amberMin, y2: greenMin })
  if (amberMax !== null && greenMax !== null && greenMax < amberMax)
    rects.push({ key: 'amber-hi', tone: 'amber', y1: greenMax, y2: amberMax })

  // Green zone on top. A missing green edge falls back to the acceptable edge (then the axis).
  rects.push({
    key: 'green',
    tone: 'green',
    y1: greenMin ?? amberMin ?? undefined,
    y2: greenMax ?? amberMax ?? undefined,
  })

  return rects
}
