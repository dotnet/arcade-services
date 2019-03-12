import { async, ComponentFixture, TestBed } from '@angular/core/testing';

import { BuildGraphTableComponent } from './build-graph-table.component';

describe('BuildGraphTableComponent', () => {
  let component: BuildGraphTableComponent;
  let fixture: ComponentFixture<BuildGraphTableComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [ BuildGraphTableComponent ]
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(BuildGraphTableComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
