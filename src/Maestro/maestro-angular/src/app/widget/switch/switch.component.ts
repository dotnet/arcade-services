import { Component, OnInit, Input, Output, EventEmitter } from '@angular/core';

@Component({
  selector: 'mc-switch',
  templateUrl: './switch.component.html',
  styleUrls: ['./switch.component.scss']
})
export class SwitchComponent {
  @Input() public style?: object;
  @Input() public theme: "primary" | "secondary" | "success" | "info" | "warning" | "danger" | "light" | "dark" = "primary";

  @Input() public value: boolean = false;
  @Output() public valueChange = new EventEmitter<boolean>();

  constructor() { }

  onValueChange(newValue: boolean) {
    if (this.value !== newValue) {
      this.value = newValue;
      this.valueChange.emit(this.value);
    }
  }
}
