import { Component, Input } from '@angular/core';

@Component({
  selector: 'progress-ring',
  template: `
    <div class="loading" [ngStyle]="style || {'width.px': width, 'height.px': height}">
      <svg class="spinner" viewBox="25 25 50 50">
        <circle class="path" cx="50" cy="50" r="20" fill="none" stroke-width="2" stroke-miterlimit="10" [attr.stroke]="color" />
      </svg>
    </div>
    `,
  styles: [`
    .loading {
      position: relative;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      overflow: hidden;
    }

    .loading .spinner {
      height: 100%;
      width: 100%;
      animation: rotate 2s linear infinite;
      transform-origin: center center;
      position: absolute;
      top: 0;
      bottom: 0;
      left: 0;
      right: 0;
      margin: auto;
    }

    .loading .spinner .path {
      stroke-dasharray: 1, 200;
      stroke-dashoffset: 0;
      animation: dash 1.5s ease-in-out infinite;
      stroke-linecap: round;
    }

    @keyframes rotate {
      100% {
        transform: rotate(360deg);
      }
    }

    @keyframes dash {
      0% {
        stroke-dasharray: 1, 200;
        stroke-dashoffset: 0;
      }

      50% {
        stroke-dasharray: 89, 200;
        stroke-dashoffset: -35px;
      }

      100% {
        stroke-dasharray: 89, 200;
        stroke-dashoffset: -124px;
      }
    }
    `]
})
export class ProgressRingComponent {
  @Input() public style?: object;
  @Input() public width: number = 50;
  @Input() public height: number = 50;
  @Input() public color: string = "#ddd";
  constructor() { }
}
