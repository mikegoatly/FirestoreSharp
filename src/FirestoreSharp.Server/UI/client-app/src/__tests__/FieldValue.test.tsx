import { render, screen } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
import { FieldValue } from '../components/FieldValue'
import type { UiValue } from '../api/types'

describe('FieldValue', () => {
  it('renders null', () => {
    const val: UiValue = { type: 'null', value: null }
    render(<FieldValue value={val} />)
    expect(screen.getByText('null')).toBeInTheDocument()
  })

  it('renders bool true', () => {
    const val: UiValue = { type: 'bool', value: true }
    render(<FieldValue value={val} />)
    expect(screen.getByText('true')).toBeInTheDocument()
  })

  it('renders bool false', () => {
    const val: UiValue = { type: 'bool', value: false }
    render(<FieldValue value={val} />)
    expect(screen.getByText('false')).toBeInTheDocument()
  })

  it('renders string with quotes', () => {
    const val: UiValue = { type: 'string', value: 'hello' }
    render(<FieldValue value={val} />)
    expect(screen.getByText('"hello"')).toBeInTheDocument()
  })

  it('renders int', () => {
    const val: UiValue = { type: 'int', value: '42' }
    render(<FieldValue value={val} />)
    expect(screen.getByText('42')).toBeInTheDocument()
  })

  it('renders double', () => {
    const val: UiValue = { type: 'double', value: 3.14 }
    render(<FieldValue value={val} />)
    expect(screen.getByText('3.14')).toBeInTheDocument()
  })

  it('renders timestamp', () => {
    const val: UiValue = { type: 'timestamp', value: '2024-01-01T00:00:00Z' }
    render(<FieldValue value={val} />)
    expect(screen.getByText('2024-01-01T00:00:00Z')).toBeInTheDocument()
  })

  it('renders bytes placeholder', () => {
    const val: UiValue = { type: 'bytes', value: 'abc' }
    render(<FieldValue value={val} />)
    expect(screen.getByText('<bytes>')).toBeInTheDocument()
  })

  it('renders array inline count', () => {
    const val: UiValue = { type: 'array', value: [{ type: 'int', value: 1 }, { type: 'int', value: 2 }] }
    render(<FieldValue value={val} inline />)
    expect(screen.getByText('[2]')).toBeInTheDocument()
  })

  it('renders map inline count', () => {
    const val: UiValue = { type: 'map', value: { a: { type: 'string', value: 'x' } } }
    render(<FieldValue value={val} inline />)
    expect(screen.getByText('{1}')).toBeInTheDocument()
  })

  it('renders geopoint', () => {
    const val: UiValue = { type: 'geopoint', value: { latitude: 51.5, longitude: -0.1 } }
    render(<FieldValue value={val} />)
    expect(screen.getByText('(51.5, -0.1)')).toBeInTheDocument()
  })
})
