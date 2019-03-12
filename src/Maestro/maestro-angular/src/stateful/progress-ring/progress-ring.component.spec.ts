import { async, ComponentFixture, TestBed } from '@angular/core/testing';

import { ProgressRingComponent } from './progress-ring.component';

describe('ProgressRingComponent', () => {
  let component: ProgressRingComponent;
  let fixture: ComponentFixture<ProgressRingComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [ ProgressRingComponent ]
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(ProgressRingComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
