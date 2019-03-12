import { Directive, ElementRef, Input, OnInit } from "@angular/core";

@Directive({
  selector: "[mcCircle]",
})
export class CircleDirective implements OnInit {
  @Input("mcCircle") public size?: string;

  public constructor(private element: ElementRef) {}
  public ngOnInit(): void {
    console.log("Original size: " + this.size);
    const size = this.size || "20px";
    this.element.nativeElement.style["border-radius"] = "50%";
    this.element.nativeElement.style.height = size;
    this.element.nativeElement.style.width = size;
  }
}
