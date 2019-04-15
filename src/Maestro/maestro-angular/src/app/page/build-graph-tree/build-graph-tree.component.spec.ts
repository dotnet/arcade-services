import { async, ComponentFixture, TestBed } from '@angular/core/testing';

import { BuildGraphTreeComponent } from './build-graph-tree.component';

describe('BuildGraphTreeComponent', () => {
  let component: BuildGraphTreeComponent;
  let fixture: ComponentFixture<BuildGraphTreeComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [ BuildGraphTreeComponent ]
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(BuildGraphTreeComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
